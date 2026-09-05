using AiDataGateway.Api.Security;
using AiDataGateway.Api.Contracts;
using AiDataGateway.Application.Maintenance;
using AiDataGateway.Application.Security;
using Microsoft.AspNetCore.Authorization;

namespace AiDataGateway.Api.Endpoints;

internal static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var settings = endpoints.MapGroup("/api/settings")
            .RequireAuthorization(new AuthorizeAttribute { Roles = GatewayRoles.Administrator });

        settings.MapGet("/maintenance", async (MaintenanceService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetAsync(cancellationToken)));

        settings.MapPut("/maintenance", async (
            UpdateMaintenanceSettingsRequest request,
            HttpContext context,
            MaintenanceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdateAsync(request, GatewayPrincipal.Actor(context.User), cancellationToken)));

        settings.MapPost("/maintenance/cleanup-now", async (
            HttpContext context,
            MaintenanceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.RunCleanupAsync(GatewayPrincipal.Actor(context.User), cancellationToken)));

        settings.MapGet("/admin-recovery", async (AdminRecoveryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetStatusAsync(cancellationToken)));

        settings.MapPut("/admin-recovery", async (
            UpdateAdminRecoveryRequest request,
            HttpContext context,
            AdminRecoveryService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdateAsync(request.NewResetPassword, GatewayPrincipal.Actor(context.User), cancellationToken)));

        return endpoints;
    }
}
