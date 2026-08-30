namespace AiDataGateway.Monitoring;

public sealed record SystemMetricSnapshot(
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
    IReadOnlyDictionary<string, double> ExtendedMetrics);
