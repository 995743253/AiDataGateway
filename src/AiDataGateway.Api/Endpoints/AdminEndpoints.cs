using System.Security.Cryptography;
using AiDataGateway.Api.Contracts;
using AiDataGateway.Api.Security;
using AiDataGateway.Application.DataSources;
using AiDataGateway.Application.Abstractions;
using AiDataGateway.Application.Security;
using AiDataGateway.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace AiDataGateway.Api.Endpoints;

internal static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/admin").RequireAuthorization(new AuthorizeAttribute { Roles = GatewayRoles.Administrator });

        admin.MapGet("/users", async (UserManager<ApplicationUser> userManager) =>
        {
            var users = await userManager.Users.OrderBy(item => item.UserName).ToListAsync();
            var result = new List<object>();
            foreach (var user in users)
            {
                result.Add(new
                {
                    user.Id,
                    user.UserName,
                    user.Email,
                    user.DisplayName,
                    user.IsEnabled,
                    user.LockoutEnd,
                    roles = await userManager.GetRolesAsync(user)
                });
            }

            return Results.Ok(result);
        });

        admin.MapPost("/users", async (CreateUserRequest request, HttpContext context, UserManager<ApplicationUser> userManager, IAuditWriter auditWriter) =>
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = request.UserName.Trim(),
                Email = request.Email.Trim(),
                DisplayName = request.DisplayName.Trim(),
                EmailConfirmed = true,
                IsEnabled = true
            };
            var result = await userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                return IdentityErrorResponse.BadRequest(result);
            }

            var roles = request.Roles.Intersect(GatewayRoles.All, StringComparer.OrdinalIgnoreCase).ToArray();
            if (roles.Length > 0)
            {
                await userManager.AddToRolesAsync(user, roles);
            }
            await auditWriter.WriteAsync(GatewayPrincipal.Actor(context.User), "user.create", "success", detail: user.UserName);
            return Results.Created($"/api/admin/users/{user.Id}", new { user.Id });
        });

        admin.MapPut("/users/{id:guid}", async (Guid id, UpdateUserRequest request, HttpContext context, UserManager<ApplicationUser> userManager, IAuditWriter auditWriter) =>
        {
            var user = await userManager.FindByIdAsync(id.ToString());
            if (user is null)
            {
                return Results.NotFound();
            }

            user.DisplayName = request.DisplayName.Trim();
            user.IsEnabled = request.Enabled;
            await userManager.UpdateAsync(user);
            var currentRoles = await userManager.GetRolesAsync(user);
            await userManager.RemoveFromRolesAsync(user, currentRoles);
            await userManager.AddToRolesAsync(user, request.Roles.Intersect(GatewayRoles.All, StringComparer.OrdinalIgnoreCase));
            if (!request.Enabled)
            {
                await userManager.UpdateSecurityStampAsync(user);
            }
            await auditWriter.WriteAsync(GatewayPrincipal.Actor(context.User), "user.update", "success", detail: user.UserName);
            return Results.NoContent();
        });

        admin.MapGet("/roles", () => Results.Ok(GatewayRoles.All));

        admin.MapGet("/oauth-clients", async (IOpenIddictApplicationManager manager) =>
        {
            var clients = new List<object>();
            await foreach (var application in manager.ListAsync())
            {
                clients.Add(new
                {
                    clientId = await manager.GetClientIdAsync(application),
                    displayName = await manager.GetDisplayNameAsync(application),
                    permissions = await manager.GetPermissionsAsync(application)
                });
            }
            return Results.Ok(clients);
        });

        admin.MapPost("/oauth-clients", async (CreateOAuthClientRequest request, HttpContext context, IOpenIddictApplicationManager manager, IAuditWriter auditWriter) =>
        {
            var clientId = $"local-ai-{Guid.NewGuid():N}";
            var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var allowedScopes = (request.Scopes ?? GatewayScopes.AiClientDefaults)
                .Intersect(GatewayScopes.AiClientDefaults, StringComparer.Ordinal)
                .ToArray();
            await manager.CreateAsync(OAuthDescriptorFactory.CreateAiClient(clientId, request.DisplayName, secret, allowedScopes));
            await auditWriter.WriteAsync(GatewayPrincipal.Actor(context.User), "oauth-client.create", "success", detail: clientId);
            return Results.Ok(new { clientId, clientSecret = secret, scopes = allowedScopes });
        });

        var dataSources = endpoints.MapGroup("/api/admin/datasources")
            .RequireAuthorization(new AuthorizeAttribute { Roles = $"{GatewayRoles.Administrator},{GatewayRoles.Operator}" });
        dataSources.MapGet("/", async (DataSourceService service, CancellationToken cancellationToken) => Results.Ok(await service.ListAsync(cancellationToken)));
        dataSources.MapPost("/", async (DataSourceUpsertRequest request, HttpContext context, DataSourceService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.CreateAsync(request, GatewayPrincipal.Actor(context.User), cancellationToken)));
        dataSources.MapPut("/{id:guid}", async (Guid id, DataSourceUpsertRequest request, HttpContext context, DataSourceService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdateAsync(id, request, GatewayPrincipal.Actor(context.User), cancellationToken)));
        dataSources.MapDelete("/{id:guid}", async (Guid id, HttpContext context, DataSourceService service, CancellationToken cancellationToken) =>
        {
            await service.DeleteAsync(id, GatewayPrincipal.Actor(context.User), cancellationToken);
            return Results.NoContent();
        });
        dataSources.MapPost("/{id:guid}/test", async (Guid id, HttpContext context, DataSourceService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.TestAsync(id, GatewayPrincipal.Actor(context.User), cancellationToken)));

        return endpoints;
    }
}
