using AiDataGateway.Domain.Monitoring;
using AiDataGateway.Monitoring;

namespace AiDataGateway.Application.Monitoring;

public sealed record MonitorTargetUpsertRequest(
    string Key,
    string Name,
    bool Enabled = true,
    Guid[]? ProjectIds = null,
    string[]? MetricKeys = null);

public sealed record MonitorTargetProjectReference(Guid Id, string Code, string Name, bool Enabled);

public sealed record MetricSampleView(
    long Id,
    DateTimeOffset CollectedAtUtc,
    double CpuPercent,
    long MemoryUsedBytes,
    long MemoryTotalBytes,
    double MemoryPercent,
    long DiskUsedBytes,
    long DiskTotalBytes,
    double DiskPercent,
    long NetworkReceivedBytes,
    long NetworkSentBytes,
    long ProcessWorkingSetBytes,
    long SystemUptimeSeconds,
    IReadOnlyDictionary<string, double> Metrics);

public sealed record MonitorTargetView(
    Guid Id,
    string Key,
    string Name,
    MonitorTargetType Type,
    bool Enabled,
    string HostName,
    string OsDescription,
    DateTimeOffset? LastSeenAtUtc,
    bool Online,
    IReadOnlyList<string> MetricKeys,
    IReadOnlyList<MonitorTargetProjectReference> Projects,
    MetricSampleView? Latest,
    DateTimeOffset UpdatedAtUtc);

public sealed record MonitorTargetCreatedView(MonitorTargetView Target, string IngestSecret);

public sealed record MonitorSecretRotatedView(Guid TargetId, string TargetKey, string IngestSecret);

public sealed record MetricIngestRequest(
    DateTimeOffset CollectedAtUtc,
    string HostName,
    string OsDescription,
    double CpuPercent,
    long MemoryUsedBytes,
    long MemoryTotalBytes,
    long DiskUsedBytes,
    long DiskTotalBytes,
    long NetworkReceivedBytes,
    long NetworkSentBytes,
    long ProcessWorkingSetBytes,
    long SystemUptimeSeconds,
    IReadOnlyDictionary<string, double>? ExtendedMetrics = null);

public sealed record MetricCatalogView(IReadOnlyList<MetricDefinition> Items, IReadOnlyList<string> RequiredKeys, IReadOnlyList<string> DefaultKeys);

public sealed record MetricIngestConfigurationView(string TargetKey, IReadOnlyList<string> MetricKeys);

public sealed record MetricQueryView(
    Guid TargetId,
    string TargetKey,
    string TargetName,
    IReadOnlyList<MetricSampleView> Items,
    int Page,
    int PageSize,
    int Total);

public sealed record MetricTrendView(
    Guid TargetId,
    string TargetKey,
    string TargetName,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int SourceCount,
    IReadOnlyList<MetricSampleView> Items);
