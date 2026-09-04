using AiDataGateway.Application.Monitoring;
using AiDataGateway.Application.Projects;
using AiDataGateway.Application.Sql;
using AiDataGateway.Application.Logs;
using AiDataGateway.Extensions;

namespace AiDataGateway.Infrastructure.Extensions;

internal sealed class GatewayExtensionHostContext(
    string actor,
    GatewayExtensionCapability capability,
    ProjectService projects,
    QueryService queries,
    LogSourceService logs,
    MonitoringService monitoring) : IGatewayExtensionContext
{
    public string Actor { get; } = actor;
    public IGatewayExtensionDatabase Database { get; } = new ExtensionDatabase(capability, actor, projects, queries);
    public IGatewayExtensionLogs Logs { get; } = new ExtensionLogs(capability, actor, logs);
    public IGatewayExtensionMonitoring Monitoring { get; } = new ExtensionMonitoring(capability, monitoring);

    private sealed class ExtensionDatabase(
        GatewayExtensionCapability capability,
        string actor,
        ProjectService projects,
        QueryService queries) : IGatewayExtensionDatabase
    {
        public async Task<IReadOnlyList<GatewayExtensionProject>> ListProjectsAsync(CancellationToken cancellationToken = default)
        {
            Demand(GatewayExtensionCapability.DataSourceRead, GatewayExtensionCapability.QueryExecute);
            return (await projects.ListAsync(cancellationToken))
                .Where(project => project.Enabled)
                .Select(project => new GatewayExtensionProject(project.Code, project.Name,
                    project.DataSources.Where(source => source.Enabled)
                        .Select(source => new GatewayExtensionDataSource(source.Key, source.Name, source.Provider)).ToArray()))
                .ToArray();
        }

        public async Task<GatewayExtensionQueryResult> QueryAsync(
            string projectCode,
            string dataSourceKey,
            string sql,
            CancellationToken cancellationToken = default)
        {
            Demand(GatewayExtensionCapability.QueryExecute);
            var project = await projects.GetByCodeAsync(projectCode, cancellationToken);
            if (!project.Enabled) throw new InvalidOperationException("Project is disabled.");
            var source = project.DataSources.FirstOrDefault(item => item.Enabled &&
                string.Equals(item.Key, dataSourceKey?.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException("No matching enabled data source is linked to this project.");
            var result = await queries.ExecuteReadAsync(source.Id, sql, actor, cancellationToken);
            return new GatewayExtensionQueryResult(result.Columns, result.Rows, result.Truncated);
        }

        private void Demand(params GatewayExtensionCapability[] accepted)
        {
            if (!accepted.Any(item => capability.HasFlag(item)))
                throw new InvalidOperationException("The extension tool did not declare the required database capability.");
        }
    }

    private sealed class ExtensionLogs(
        GatewayExtensionCapability capability,
        string actor,
        LogSourceService logs) : IGatewayExtensionLogs
    {
        public async Task<GatewayExtensionLogResult> QueryAsync(
            string projectCode,
            string? logSourceKey = null,
            string? searchText = null,
            string? level = null,
            DateTimeOffset? fromUtc = null,
            DateTimeOffset? toUtc = null,
            int count = 100,
            CancellationToken cancellationToken = default)
        {
            if (!capability.HasFlag(GatewayExtensionCapability.LogRead))
                throw new InvalidOperationException("This extension tool did not declare log-read capability.");
            var result = await logs.QueryByProjectAsync(new ProjectLogQueryRequest(projectCode, logSourceKey,
                Level: level, FromUtc: fromUtc, ToUtc: toUtc, SearchText: searchText,
                Page: 1, PageSize: Math.Clamp(count, 1, 500)), actor, cancellationToken);
            return new GatewayExtensionLogResult(result.ProjectCode, result.LogSourceKey, result.LogSourceName,
                result.Total, result.IsPartial, result.Warning,
                result.Items.Select(item => new GatewayExtensionLogEvent(item.Id, item.TimestampUtc, item.Level,
                    item.Message, item.Exception, item.Properties, item.RawText, item.Incomplete, item.ParseWarning)).ToArray());
        }
    }

    private sealed class ExtensionMonitoring(
        GatewayExtensionCapability capability,
        MonitoringService monitoring) : IGatewayExtensionMonitoring
    {
        public async Task<IReadOnlyList<GatewayExtensionMonitorTarget>> ListTargetsAsync(CancellationToken cancellationToken = default)
        {
            Demand();
            return (await monitoring.ListTargetsAsync(cancellationToken)).Where(item => item.Enabled)
                .Select(item => new GatewayExtensionMonitorTarget(item.Key, item.Name, item.Online, item.HostName, item.OsDescription))
                .ToArray();
        }

        public async Task<GatewayExtensionMetricResult> QueryAsync(
            string targetKey,
            DateTimeOffset? fromUtc = null,
            DateTimeOffset? toUtc = null,
            int maxPoints = 200,
            CancellationToken cancellationToken = default)
        {
            Demand();
            var target = (await monitoring.ListTargetsAsync(cancellationToken)).FirstOrDefault(item => item.Enabled &&
                string.Equals(item.Key, targetKey?.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException("Monitor target was not found.");
            var result = await monitoring.QueryTrendAsync(target.Id, fromUtc, toUtc, maxPoints, cancellationToken);
            return new GatewayExtensionMetricResult(result.TargetKey, result.TargetName, result.FromUtc, result.ToUtc,
                result.SourceCount, result.Items.Select(item => new GatewayExtensionMetricSample(item.CollectedAtUtc, item.Metrics)).ToArray());
        }

        private void Demand()
        {
            if (!capability.HasFlag(GatewayExtensionCapability.MetricsRead))
                throw new InvalidOperationException("The extension tool did not declare the metrics capability.");
        }
    }
}
