using System.Text.Json;
using AiDataGateway.Application.Abstractions;
using AiDataGateway.Application.Sql;
using AiDataGateway.Domain.Approvals;
using AiDataGateway.Domain.DataSources;

namespace AiDataGateway.Application.Approvals;

public sealed record ChangeSubmissionResult(Guid Id, ChangeStatus Status, SqlAnalysis Analysis, DateTimeOffset ExpiresAtUtc);

public sealed class ChangeSubmissionService(
    ISqlSafetyAnalyzer analyzer,
    IDataSourceRepository dataSources,
    IChangeRequestRepository changes,
    IMaintenanceSettingsRepository settings,
    IAuditWriter auditWriter)
{
    public async Task<ChangeSubmissionResult> SubmitAsync(
        Guid dataSourceId,
        string sql,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var analysis = analyzer.Analyze(sql);
        if (analysis.IsReadOnly || !analysis.Allowed)
        {
            throw new InvalidOperationException("The SQL is not an approvable write statement.");
        }

        var source = await dataSources.FindAsync(dataSourceId, cancellationToken)
            ?? throw new KeyNotFoundException("Data source was not found.");
        if (!source.Enabled || source.AccessMode is DataSourceAccessMode.Disabled or DataSourceAccessMode.ReadOnly)
        {
            throw new InvalidOperationException("The data source does not allow write requests.");
        }

        var systemSettings = await settings.GetAsync(cancellationToken);
        var change = new ChangeRequest(
            source.Id,
            sql,
            actor,
            analysis.RiskLevel,
            systemSettings.ApprovalExpirationMinutes);
        await changes.AddAsync(change, cancellationToken);
        await changes.SaveChangesAsync(cancellationToken);
        await auditWriter.WriteAsync(actor, "change.submit", "pending", source.Id, JsonSerializer.Serialize(new
        {
            changeId = change.Id,
            sql = change.Sql,
            operation = analysis.Operation,
            riskLevel = analysis.RiskLevel.ToString(),
            expiresAtUtc = change.ExpiresAtUtc
        }), cancellationToken);

        return new ChangeSubmissionResult(change.Id, change.Status, analysis, change.ExpiresAtUtc);
    }
}
