using AiDataGateway.Api.Contracts;
using AiDataGateway.Api.Security;
using AiDataGateway.Application.Abstractions;
using AiDataGateway.Application.DataSources;
using AiDataGateway.Application.Security;
using AiDataGateway.Application.Sql;
using AiDataGateway.Domain.Approvals;
using AiDataGateway.Domain.DataSources;
using Microsoft.AspNetCore.Authorization;

namespace AiDataGateway.Api.Endpoints;

internal static class GatewayEndpoints
{
    public static IEndpointRouteBuilder MapGatewayEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var gateway = endpoints.MapGroup("/api/gateway").RequireAuthorization();

        gateway.MapGet("/datasources", async (HttpContext context, DataSourceService service, CancellationToken cancellationToken) =>
        {
            if (!GatewayPrincipal.Can(context.User, GatewayScopes.DataSourceRead, GatewayRoles.Developer, GatewayRoles.Viewer, GatewayRoles.Operator))
            {
                return Results.Forbid();
            }

            var items = await service.ListAsync(cancellationToken);
            return Results.Ok(items.Where(item => item.Enabled).Select(item => new { item.Id, item.Key, item.Name, item.Provider, item.AccessMode }));
        });

        gateway.MapPost("/sql/validate", (ValidateSqlRequest request, QueryService service) => Results.Ok(service.Validate(request.Sql)));

        gateway.MapPost("/query", async (ExecuteQueryRequest request, HttpContext context, QueryService service, CancellationToken cancellationToken) =>
        {
            if (!GatewayPrincipal.Can(context.User, GatewayScopes.QueryExecute, GatewayRoles.Developer, GatewayRoles.Operator))
            {
                return Results.Forbid();
            }

            return Results.Ok(await service.ExecuteReadAsync(request.DataSourceId, request.Sql, GatewayPrincipal.Actor(context.User), cancellationToken));
        });

        gateway.MapPost("/changes", async (
            SubmitChangeRequest request,
            HttpContext context,
            ISqlSafetyAnalyzer analyzer,
            IDataSourceRepository dataSources,
            IChangeRequestRepository changes,
            IAuditWriter auditWriter,
            CancellationToken cancellationToken) =>
        {
            if (!GatewayPrincipal.Can(context.User, GatewayScopes.ChangeSubmit, GatewayRoles.Developer, GatewayRoles.Operator))
            {
                return Results.Forbid();
            }

            var analysis = analyzer.Analyze(request.Sql);
            if (analysis.IsReadOnly || !analysis.Allowed)
            {
                return Results.BadRequest(new { message = "The SQL is not an approvable write statement.", analysis });
            }

            var source = await dataSources.FindAsync(request.DataSourceId, cancellationToken);
            if (source is null)
            {
                return Results.NotFound();
            }
            if (!source.Enabled || source.AccessMode is DataSourceAccessMode.Disabled or DataSourceAccessMode.ReadOnly)
            {
                return Results.BadRequest(new { message = "The data source does not allow write requests." });
            }

            var actor = GatewayPrincipal.Actor(context.User);
            var change = new ChangeRequest(source.Id, request.Sql, actor, analysis.RiskLevel);
            await changes.AddAsync(change, cancellationToken);
            await changes.SaveChangesAsync(cancellationToken);
            await auditWriter.WriteAsync(actor, "change.submit", "pending", source.Id, change.Id.ToString(), cancellationToken);
            return Results.Accepted($"/api/gateway/changes/{change.Id}", new { change.Id, change.Status, analysis });
        });

        var approvals = endpoints.MapGroup("/api/approvals")
            .RequireAuthorization(new AuthorizeAttribute { Roles = $"{GatewayRoles.Administrator},{GatewayRoles.Approver}" });
        approvals.MapGet("/", ListApprovalsAsync);
        approvals.MapGet("/pending", ListPendingApprovalsAsync);
        approvals.MapGet("/{id:guid}", GetApprovalAsync);
        approvals.MapPost("/{id:guid}/review", ReviewAsync);

        endpoints.MapGet("/api/audit/logs", ListAuditLogsAsync)
            .RequireAuthorization(new AuthorizeAttribute { Roles = $"{GatewayRoles.Administrator},{GatewayRoles.Approver},{GatewayRoles.Operator},{GatewayRoles.Auditor}" });

        return endpoints;
    }

    private static async Task<IResult> ListApprovalsAsync(
        string? status,
        int? take,
        IChangeRequestRepository changes,
        IDataSourceRepository dataSources,
        CancellationToken cancellationToken)
    {
        ChangeStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            if (!Enum.TryParse<ChangeStatus>(status, true, out var parsedStatus))
            {
                return Results.BadRequest(new { message = $"Unknown approval status '{status}'." });
            }
            statusFilter = parsedStatus;
        }

        var items = await changes.ListAsync(statusFilter, take ?? 200, cancellationToken);
        var sourceNames = (await dataSources.ListAsync(cancellationToken)).ToDictionary(item => item.Id, item => item.Name);
        return Results.Ok(items.Select(item => ToApprovalView(item, sourceNames.GetValueOrDefault(item.DataSourceId))));
    }

    private static async Task<IResult> ListPendingApprovalsAsync(
        IChangeRequestRepository changes,
        IDataSourceRepository dataSources,
        CancellationToken cancellationToken)
    {
        var items = await changes.ListPendingAsync(cancellationToken);
        var sourceNames = (await dataSources.ListAsync(cancellationToken)).ToDictionary(item => item.Id, item => item.Name);
        return Results.Ok(items.Select(item => ToApprovalView(item, sourceNames.GetValueOrDefault(item.DataSourceId))));
    }

    private static async Task<IResult> GetApprovalAsync(
        Guid id,
        IChangeRequestRepository changes,
        IDataSourceRepository dataSources,
        CancellationToken cancellationToken)
    {
        var item = await changes.FindAsync(id, cancellationToken);
        if (item is null)
        {
            return Results.NotFound();
        }

        var source = await dataSources.FindAsync(item.DataSourceId, cancellationToken);
        return Results.Ok(ToApprovalView(item, source?.Name));
    }

    private static async Task<IResult> ListAuditLogsAsync(
        int? take,
        IAuditLogReader auditLogs,
        IDataSourceRepository dataSources,
        CancellationToken cancellationToken)
    {
        var items = await auditLogs.ListRecentAsync(take ?? 200, cancellationToken);
        var sourceNames = (await dataSources.ListAsync(cancellationToken)).ToDictionary(item => item.Id, item => item.Name);
        return Results.Ok(items.Select(item => new
        {
            item.Id,
            item.Actor,
            item.Action,
            item.Outcome,
            item.DataSourceId,
            dataSourceName = item.DataSourceId.HasValue ? sourceNames.GetValueOrDefault(item.DataSourceId.Value) : null,
            item.Detail,
            item.CreatedAtUtc
        }));
    }

    private static object ToApprovalView(ChangeRequest item, string? dataSourceName)
    {
        var status = item.Status == ChangeStatus.Pending && item.ExpiresAtUtc <= DateTimeOffset.UtcNow
            ? ChangeStatus.Expired
            : item.Status;
        return new
        {
            item.Id,
            item.DataSourceId,
            dataSourceName,
            item.Sql,
            item.RequestedBy,
            item.ReviewedBy,
            item.ReviewComment,
            riskLevel = item.RiskLevel.ToString(),
            status = status.ToString(),
            item.CreatedAtUtc,
            item.ExpiresAtUtc,
            item.ReviewedAtUtc,
            item.ExecutedAtUtc,
            item.ExecutionError
        };
    }

    private static async Task<IResult> ReviewAsync(
        Guid id,
        ReviewChangeRequest review,
        HttpContext context,
        IChangeRequestRepository changes,
        IDataSourceRepository dataSources,
        ICredentialProtector protector,
        IDatabaseAdapterFactory adapterFactory,
        ISqlSafetyAnalyzer analyzer,
        IAuditWriter auditWriter,
        CancellationToken cancellationToken)
    {
        var change = await changes.FindAsync(id, cancellationToken);
        if (change is null)
        {
            return Results.NotFound();
        }

        var actor = GatewayPrincipal.Actor(context.User);
        if (!review.Approved)
        {
            change.Reject(actor, review.Comment);
            await changes.SaveChangesAsync(cancellationToken);
            await auditWriter.WriteAsync(actor, "change.review", "rejected", change.DataSourceId, change.Id.ToString(), cancellationToken);
            return Results.Ok(new { change.Id, change.Status });
        }

        var source = await dataSources.FindAsync(change.DataSourceId, cancellationToken);
        if (source is null || !source.Enabled || source.AccessMode is DataSourceAccessMode.Disabled or DataSourceAccessMode.ReadOnly)
        {
            return Results.BadRequest(new { message = "The data source is unavailable or read-only." });
        }

        var analysis = analyzer.Analyze(change.Sql);
        if (!analysis.Allowed || analysis.IsReadOnly)
        {
            return Results.BadRequest(new { message = "The stored SQL no longer passes the active policy.", analysis });
        }

        change.Approve(actor, review.Comment);
        await changes.SaveChangesAsync(cancellationToken);
        try
        {
            var connection = new DatabaseConnection(source.Host, source.Port, source.Database, source.Username,
                protector.Unprotect(source.ProtectedPassword), source.CommandTimeoutSeconds);
            var affectedRows = await adapterFactory.Get(source.Provider).ExecuteAsync(connection, change.Sql, cancellationToken);
            change.MarkExecuted(true, null);
            await changes.SaveChangesAsync(cancellationToken);
            await auditWriter.WriteAsync(actor, "change.execute", "success", source.Id, $"change={change.Id};rows={affectedRows}", cancellationToken);
            return Results.Ok(new { change.Id, change.Status, affectedRows });
        }
        catch (Exception exception)
        {
            change.MarkExecuted(false, exception.Message);
            await changes.SaveChangesAsync(cancellationToken);
            await auditWriter.WriteAsync(actor, "change.execute", "failure", source.Id, exception.Message, cancellationToken);
            return Results.Problem(exception.Message);
        }
    }
}
