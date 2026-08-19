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
        approvals.MapGet("/pending", async (IChangeRequestRepository changes, CancellationToken cancellationToken) =>
            Results.Ok(await changes.ListPendingAsync(cancellationToken)));
        approvals.MapPost("/{id:guid}/review", ReviewAsync);

        return endpoints;
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
