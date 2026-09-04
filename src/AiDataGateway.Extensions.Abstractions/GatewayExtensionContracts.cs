using System.Text.Json;

namespace AiDataGateway.Extensions;

/// <summary>Public extension ABI information.</summary>
public static class GatewayExtensionContract
{
    public const int Version = 1;
}

/// <summary>Entry point implemented by a private customization assembly.</summary>
public interface IGatewayExtension
{
    GatewayExtensionDefinition Definition { get; }
    Task<JsonElement> InvokeAsync(
        string operation,
        JsonElement arguments,
        IGatewayExtensionContext context,
        CancellationToken cancellationToken);
}

/// <summary>Services supplied by the gateway for the current invocation.</summary>
public interface IGatewayExtensionContext
{
    string Actor { get; }
    IGatewayExtensionDatabase Database { get; }
    IGatewayExtensionLogs Logs { get; }
    IGatewayExtensionMonitoring Monitoring { get; }
}

/// <summary>
/// Project-scoped database access. The gateway keeps credentials private and applies its normal
/// read-only SQL checks, table blacklist, row limit and audit logging.
/// </summary>
public interface IGatewayExtensionDatabase
{
    Task<IReadOnlyList<GatewayExtensionProject>> ListProjectsAsync(CancellationToken cancellationToken = default);
    Task<GatewayExtensionQueryResult> QueryAsync(
        string projectCode,
        string dataSourceKey,
        string sql,
        CancellationToken cancellationToken = default);
}

/// <summary>Read access to stored local or remote monitoring samples.</summary>
public interface IGatewayExtensionMonitoring
{
    Task<IReadOnlyList<GatewayExtensionMonitorTarget>> ListTargetsAsync(CancellationToken cancellationToken = default);
    Task<GatewayExtensionMetricResult> QueryAsync(
        string targetKey,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        int maxPoints = 200,
        CancellationToken cancellationToken = default);
}

/// <summary>Project-scoped read access to local NLog, Seq, or remote-agent events.</summary>
public interface IGatewayExtensionLogs
{
    Task<GatewayExtensionLogResult> QueryAsync(
        string projectCode,
        string? logSourceKey = null,
        string? searchText = null,
        string? level = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        int count = 100,
        CancellationToken cancellationToken = default);
}

public sealed record GatewayExtensionDefinition(
    string Id,
    string Name,
    string Version,
    string Description,
    string? PageTitle,
    string? FrontendEntry,
    IReadOnlyList<GatewayExtensionToolDefinition> Tools);

public sealed record GatewayExtensionToolDefinition(
    string Name,
    string Description,
    JsonElement InputSchema,
    GatewayExtensionCapability Capability = GatewayExtensionCapability.None,
    bool VisibleInUi = true,
    bool ReadOnly = true);

[Flags]
public enum GatewayExtensionCapability
{
    None = 0,
    DataSourceRead = 1,
    QueryExecute = 2,
    LogRead = 4,
    MetricsRead = 8
}

public sealed record GatewayExtensionProject(
    string Code,
    string Name,
    IReadOnlyList<GatewayExtensionDataSource> DataSources);

public sealed record GatewayExtensionDataSource(string Key, string Name, string Provider);

public sealed record GatewayExtensionQueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    bool Truncated);

public sealed record GatewayExtensionMonitorTarget(string Key, string Name, bool Online, string HostName, string OsDescription);

public sealed record GatewayExtensionMetricSample(
    DateTimeOffset CollectedAtUtc,
    IReadOnlyDictionary<string, double> Metrics);

public sealed record GatewayExtensionMetricResult(
    string TargetKey,
    string TargetName,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int SourceCount,
    IReadOnlyList<GatewayExtensionMetricSample> Items);

public sealed record GatewayExtensionLogEvent(
    string Id,
    DateTimeOffset? TimestampUtc,
    string? Level,
    string? Message,
    string? Exception,
    IReadOnlyDictionary<string, object?> Properties,
    string RawText,
    bool Incomplete,
    string? ParseWarning);

public sealed record GatewayExtensionLogResult(
    string ProjectCode,
    string LogSourceKey,
    string LogSourceName,
    int Total,
    bool Partial,
    string? Warning,
    IReadOnlyList<GatewayExtensionLogEvent> Items);
