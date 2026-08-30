using AiDataGateway.Api.Security;
using AiDataGateway.Application.Monitoring;
using AiDataGateway.Application.Security;
using Microsoft.AspNetCore.Authorization;

namespace AiDataGateway.Api.Endpoints;

internal static class MonitoringEndpoints
{
    public static IEndpointRouteBuilder MapMonitoringEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/admin/monitoring/targets")
            .RequireAuthorization(new AuthorizeAttribute { Roles = $"{GatewayRoles.Administrator},{GatewayRoles.Operator}" });
        admin.MapPost("/", async (MonitorTargetUpsertRequest request, HttpContext context, MonitoringService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.CreateRemoteAsync(request, GatewayPrincipal.Actor(context.User), cancellationToken)));
        admin.MapPut("/{id:guid}", async (Guid id, MonitorTargetUpsertRequest request, HttpContext context, MonitoringService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdateAsync(id, request, GatewayPrincipal.Actor(context.User), cancellationToken)));
        admin.MapDelete("/{id:guid}", async (Guid id, HttpContext context, MonitoringService service, CancellationToken cancellationToken) =>
        {
            await service.DeleteAsync(id, GatewayPrincipal.Actor(context.User), cancellationToken);
            return Results.NoContent();
        });
        admin.MapPost("/{id:guid}/rotate-secret", async (Guid id, HttpContext context, MonitoringService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.RotateSecretAsync(id, GatewayPrincipal.Actor(context.User), cancellationToken)));

        endpoints.MapGet("/api/monitoring/targets", async (HttpContext context, MonitoringService service, CancellationToken cancellationToken) =>
        {
            if (!CanRead(context)) return Results.Forbid();
            return Results.Ok(await service.ListTargetsAsync(cancellationToken));
        }).RequireAuthorization();

        endpoints.MapGet("/api/monitoring/metric-catalog", (HttpContext context, MonitoringService service) =>
        {
            if (!CanRead(context)) return Results.Forbid();
            return Results.Ok(service.GetMetricCatalog());
        }).RequireAuthorization();

        endpoints.MapGet("/api/monitoring/targets/{id:guid}/samples", async (
            Guid id, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int page, int pageSize,
            HttpContext context, MonitoringService service, CancellationToken cancellationToken) =>
        {
            if (!CanRead(context)) return Results.Forbid();
            return Results.Ok(await service.QueryAsync(id, fromUtc, toUtc, page, pageSize <= 0 ? 100 : pageSize, cancellationToken));
        }).RequireAuthorization();

        endpoints.MapGet("/api/monitoring/targets/{id:guid}/trend", async (
            Guid id, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int maxPoints,
            HttpContext context, MonitoringService service, CancellationToken cancellationToken) =>
        {
            if (!CanRead(context)) return Results.Forbid();
            return Results.Ok(await service.QueryTrendAsync(id, fromUtc, toUtc, maxPoints <= 0 ? 400 : maxPoints, cancellationToken));
        }).RequireAuthorization();

        endpoints.MapGet("/api/gateway/projects/{projectCode}/metrics", async (
            string projectCode, string? targetKey, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int count,
            HttpContext context, MonitoringService service, CancellationToken cancellationToken) =>
        {
            if (!CanRead(context)) return Results.Forbid();
            return Results.Ok(await service.QueryByProjectAsync(projectCode, targetKey, fromUtc, toUtc, count <= 0 ? 100 : count, cancellationToken));
        }).RequireAuthorization();

        endpoints.MapPost("/api/monitoring/ingest/{key}", async (
            string key, MetricIngestRequest request, HttpContext context, MonitoringService service, CancellationToken cancellationToken) =>
        {
            var accepted = await service.IngestRemoteAsync(key, context.Request.Headers["X-Monitor-Key"].ToString(), request, cancellationToken);
            return accepted ? Results.Accepted() : Results.Unauthorized();
        });

        endpoints.MapGet("/api/monitoring/ingest/{key}/configuration", async (
            string key, HttpContext context, MonitoringService service, CancellationToken cancellationToken) =>
        {
            var configuration = await service.GetIngestConfigurationAsync(key, context.Request.Headers["X-Monitor-Key"].ToString(), cancellationToken);
            return configuration is null ? Results.Unauthorized() : Results.Ok(configuration);
        });

        return endpoints;
    }

    private static bool CanRead(HttpContext context) =>
        GatewayPrincipal.Can(context.User, GatewayScopes.MetricsRead,
            GatewayRoles.Operator, GatewayRoles.Auditor, GatewayRoles.Approver, GatewayRoles.Viewer);
}
