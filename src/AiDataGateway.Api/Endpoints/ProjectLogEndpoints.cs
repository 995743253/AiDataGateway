using AiDataGateway.Api.Security;
using AiDataGateway.Application.Logs;
using AiDataGateway.Application.Projects;
using AiDataGateway.Application.Security;
using Microsoft.AspNetCore.Authorization;
using System.Text.Json;

namespace AiDataGateway.Api.Endpoints;

internal static class ProjectLogEndpoints
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapProjectLogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var projects = endpoints.MapGroup("/api/admin/projects")
            .RequireAuthorization(new AuthorizeAttribute { Roles = $"{GatewayRoles.Administrator},{GatewayRoles.Operator}" });
        projects.MapGet("/", async (ProjectService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(cancellationToken)));
        projects.MapGet("/{id:guid}", async (Guid id, ProjectService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetAsync(id, cancellationToken)));
        projects.MapPost("/", async (ProjectUpsertRequest request, HttpContext context, ProjectService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.CreateAsync(request, GatewayPrincipal.Actor(context.User), cancellationToken)));
        projects.MapPut("/{id:guid}", async (Guid id, ProjectUpsertRequest request, HttpContext context, ProjectService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdateAsync(id, request, GatewayPrincipal.Actor(context.User), cancellationToken)));
        projects.MapDelete("/{id:guid}", async (Guid id, HttpContext context, ProjectService service, CancellationToken cancellationToken) =>
        {
            await service.DeleteAsync(id, GatewayPrincipal.Actor(context.User), cancellationToken);
            return Results.NoContent();
        });

        var logSources = endpoints.MapGroup("/api/admin/log-sources")
            .RequireAuthorization(new AuthorizeAttribute { Roles = $"{GatewayRoles.Administrator},{GatewayRoles.Operator}" });
        logSources.MapGet("/", async (LogSourceService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(true, cancellationToken)));
        logSources.MapPost("/", async (LogSourceUpsertRequest request, HttpContext context, LogSourceService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.CreateAsync(request, GatewayPrincipal.Actor(context.User), cancellationToken)));
        logSources.MapPut("/{id:guid}", async (Guid id, LogSourceUpsertRequest request, HttpContext context, LogSourceService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdateAsync(id, request, GatewayPrincipal.Actor(context.User), cancellationToken)));
        logSources.MapDelete("/{id:guid}", async (Guid id, HttpContext context, LogSourceService service, CancellationToken cancellationToken) =>
        {
            await service.DeleteAsync(id, GatewayPrincipal.Actor(context.User), cancellationToken);
            return Results.NoContent();
        });
        logSources.MapPost("/{id:guid}/test", async (Guid id, HttpContext context, LogSourceService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.TestAsync(id, GatewayPrincipal.Actor(context.User), cancellationToken)));

        endpoints.MapGet("/api/log-sources", ListReadableLogSourcesAsync).RequireAuthorization();
        endpoints.MapPost("/api/logs/query", QueryLogsAsync).RequireAuthorization();
        endpoints.MapGet("/api/logs/stream", StreamLogsAsync).RequireAuthorization();
        endpoints.MapGet("/api/gateway/projects/{code}", GetProjectForAiAsync).RequireAuthorization();
        endpoints.MapPost("/api/gateway/logs/query", QueryProjectLogsForAiAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task StreamLogsAsync(
        Guid logSourceId,
        string? query,
        string? level,
        string? searchText,
        string? propertyName,
        string? propertyValue,
        DateTimeOffset? fromUtc,
        HttpContext context,
        LogSourceService service,
        CancellationToken cancellationToken)
    {
        if (!GatewayPrincipal.Can(context.User, GatewayScopes.LogRead,
                GatewayRoles.Operator, GatewayRoles.Auditor, GatewayRoles.Approver))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        context.Response.ContentType = "text/event-stream; charset=utf-8";
        await foreach (var item in service.StreamAsync(new LogQueryRequest(logSourceId, query, level, fromUtc, null,
                           searchText, propertyName, propertyValue, 1, 200), GatewayPrincipal.Actor(context.User), cancellationToken))
        {
            await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(item, WebJson)}\n\n", cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);
        }
    }

    private static async Task<IResult> ListReadableLogSourcesAsync(
        HttpContext context,
        LogSourceService service,
        CancellationToken cancellationToken)
    {
        if (!GatewayPrincipal.Can(context.User, GatewayScopes.LogRead,
                GatewayRoles.Operator, GatewayRoles.Auditor, GatewayRoles.Approver))
        {
            return Results.Forbid();
        }

        return Results.Ok(await service.ListAsync(false, cancellationToken));
    }

    private static async Task<IResult> QueryLogsAsync(
        LogQueryRequest request,
        HttpContext context,
        LogSourceService service,
        CancellationToken cancellationToken)
    {
        if (!GatewayPrincipal.Can(context.User, GatewayScopes.LogRead,
                GatewayRoles.Operator, GatewayRoles.Auditor, GatewayRoles.Approver))
        {
            return Results.Forbid();
        }

        return Results.Ok(await service.QueryAsync(request, GatewayPrincipal.Actor(context.User), cancellationToken));
    }

    private static async Task<IResult> GetProjectForAiAsync(
        string code,
        HttpContext context,
        ProjectService service,
        CancellationToken cancellationToken)
    {
        if (!GatewayPrincipal.Can(context.User, GatewayScopes.DataSourceRead, GatewayRoles.Operator, GatewayRoles.Viewer))
        {
            return Results.Forbid();
        }

        var project = await service.GetByCodeAsync(code, cancellationToken);
        return project.Enabled ? Results.Ok(project) : Results.NotFound();
    }

    private static async Task<IResult> QueryProjectLogsForAiAsync(
        ProjectLogQueryRequest request,
        HttpContext context,
        LogSourceService service,
        CancellationToken cancellationToken)
    {
        var allowed = GatewayPrincipal.Can(context.User, GatewayScopes.LogRead,
                          GatewayRoles.Operator, GatewayRoles.Auditor, GatewayRoles.Approver) ||
                      GatewayPrincipal.Can(context.User, GatewayScopes.QueryExecute);
        if (!allowed)
        {
            return Results.Forbid();
        }

        return Results.Ok(await service.QueryByProjectAsync(request, GatewayPrincipal.Actor(context.User), cancellationToken));
    }
}
