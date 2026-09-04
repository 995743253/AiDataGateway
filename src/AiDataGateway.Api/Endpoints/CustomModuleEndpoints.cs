using System.Text.Json;
using AiDataGateway.Api.Security;
using AiDataGateway.Application.Security;
using AiDataGateway.Extensions;
using AiDataGateway.Infrastructure.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.StaticFiles;

namespace AiDataGateway.Api.Endpoints;

internal static class CustomModuleEndpoints
{
    private const long MaximumUploadBytes = 100 * 1024 * 1024;
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    public static IEndpointRouteBuilder MapCustomModuleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/custom-modules", ListAsync).RequireAuthorization();
        endpoints.MapPost("/api/custom-modules/{id}/invoke/{operation}", InvokeAsync).RequireAuthorization();
        endpoints.MapGet("/custom-modules/{id}/ui/{**path}", GetAssetAsync).RequireAuthorization();

        var admin = endpoints.MapGroup("/api/admin/custom-modules")
            .RequireAuthorization(new AuthorizeAttribute { Roles = GatewayRoles.Administrator });
        admin.MapPost("/install", InstallAsync).DisableAntiforgery();
        admin.MapPut("/{id}/enabled", SetEnabledAsync);
        admin.MapDelete("/{id}", DeleteAsync);
        return endpoints;
    }

    private static IResult ListAsync(HttpContext context, GatewayExtensionManager manager)
    {
        var isAdministrator = context.User.IsInRole(GatewayRoles.Administrator);
        var modules = manager.List()
            .Where(item => item.Enabled || isAdministrator)
            .Select(item =>
            {
                var allowedTools = item.Tools.Where(tool => CanUse(context, tool.Capability)).ToArray();
                var pageAllowed = item.Tools.Count == 0 || allowedTools.Length > 0;
                return item with { Tools = allowedTools, PageUrl = pageAllowed ? item.PageUrl : null };
            });
        return Results.Ok(modules);
    }

    private static async Task<IResult> InstallAsync(
        IFormFile package,
        HttpContext context,
        GatewayExtensionManager manager,
        CancellationToken cancellationToken)
    {
        if (package.Length <= 0 || package.Length > MaximumUploadBytes) return Results.BadRequest(new { message = "扩展包必须小于等于 100 MB。" });
        if (!string.Equals(Path.GetExtension(package.FileName), ".zip", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest(new { message = "请上传 .zip 扩展包。" });
        await using var stream = package.OpenReadStream();
        return Results.Ok(await manager.InstallAsync(stream, GatewayPrincipal.Actor(context.User), cancellationToken));
    }

    private static async Task<IResult> SetEnabledAsync(
        string id,
        SetCustomModuleEnabledRequest request,
        HttpContext context,
        GatewayExtensionManager manager,
        CancellationToken cancellationToken) =>
        Results.Ok(await manager.SetEnabledAsync(id, request.Enabled, GatewayPrincipal.Actor(context.User), cancellationToken));

    private static async Task<IResult> DeleteAsync(
        string id,
        HttpContext context,
        GatewayExtensionManager manager,
        CancellationToken cancellationToken)
    {
        await manager.DeleteAsync(id, GatewayPrincipal.Actor(context.User), cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> InvokeAsync(
        string id,
        string operation,
        JsonElement arguments,
        HttpContext context,
        GatewayExtensionManager manager,
        CancellationToken cancellationToken)
    {
        var module = manager.List().FirstOrDefault(item => item.Enabled && item.Loaded && string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        var tool = module?.Tools.FirstOrDefault(item => item.VisibleInUi && string.Equals(item.Name, operation, StringComparison.Ordinal));
        if (tool is null) return Results.NotFound();
        if (!CanUse(context, tool.Capability)) return Results.Forbid();
        return Results.Ok(await manager.InvokeAsync(id, operation, arguments, GatewayPrincipal.Actor(context.User), true, cancellationToken));
    }

    private static IResult GetAssetAsync(string id, string? path, HttpContext context, GatewayExtensionManager manager)
    {
        var module = manager.List().FirstOrDefault(item => item.Enabled && item.Loaded && string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        if (module is null) return Results.NotFound();
        if (module.Tools.Count > 0 && !module.Tools.Any(tool => CanUse(context, tool.Capability))) return Results.Forbid();
        if (!manager.TryResolveAsset(id, path, out var fullPath)) return Results.NotFound();
        var contentType = ContentTypes.TryGetContentType(fullPath, out var detected) ? detected : "application/octet-stream";
        context.Response.Headers.CacheControl = fullPath.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ? "no-cache" : "public,max-age=31536000,immutable";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; connect-src 'self'; font-src 'self' data:; frame-ancestors 'self'";
        return Results.File(fullPath, contentType);
    }

    internal static bool CanUse(HttpContext context, GatewayExtensionCapability capability)
    {
        if (context.User.Identity?.IsAuthenticated != true) return false;
        var known = GatewayExtensionCapability.DataSourceRead | GatewayExtensionCapability.QueryExecute |
                    GatewayExtensionCapability.LogRead | GatewayExtensionCapability.MetricsRead;
        if ((capability & ~known) != 0) return false;
        return (!capability.HasFlag(GatewayExtensionCapability.DataSourceRead) ||
                GatewayPrincipal.Can(context.User, GatewayScopes.DataSourceRead, GatewayRoles.Developer, GatewayRoles.Viewer, GatewayRoles.Operator)) &&
               (!capability.HasFlag(GatewayExtensionCapability.QueryExecute) ||
                GatewayPrincipal.Can(context.User, GatewayScopes.QueryExecute, GatewayRoles.Developer, GatewayRoles.Operator)) &&
               (!capability.HasFlag(GatewayExtensionCapability.LogRead) ||
                GatewayPrincipal.Can(context.User, GatewayScopes.LogRead, GatewayRoles.Operator, GatewayRoles.Auditor, GatewayRoles.Approver)) &&
               (!capability.HasFlag(GatewayExtensionCapability.MetricsRead) ||
                GatewayPrincipal.Can(context.User, GatewayScopes.MetricsRead, GatewayRoles.Operator, GatewayRoles.Auditor, GatewayRoles.Approver, GatewayRoles.Viewer));
    }

    private sealed record SetCustomModuleEnabledRequest(bool Enabled);
}
