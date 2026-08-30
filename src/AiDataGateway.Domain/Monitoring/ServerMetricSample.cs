namespace AiDataGateway.Domain.Monitoring;

public sealed class ServerMetricSample
{
    private ServerMetricSample()
    {
    }

    public ServerMetricSample(
        Guid monitorTargetId,
        DateTimeOffset collectedAtUtc,
        double cpuPercent,
        long memoryUsedBytes,
        long memoryTotalBytes,
        long diskUsedBytes,
        long diskTotalBytes,
        long networkReceivedBytes,
        long networkSentBytes,
        long processWorkingSetBytes,
        long systemUptimeSeconds,
        string? extendedMetricsJson = null)
    {
        MonitorTargetId = monitorTargetId;
        CollectedAtUtc = collectedAtUtc;
        CpuPercent = Math.Round(Math.Clamp(cpuPercent, 0, 100), 2);
        MemoryUsedBytes = NonNegative(memoryUsedBytes);
        MemoryTotalBytes = NonNegative(memoryTotalBytes);
        DiskUsedBytes = NonNegative(diskUsedBytes);
        DiskTotalBytes = NonNegative(diskTotalBytes);
        NetworkReceivedBytes = NonNegative(networkReceivedBytes);
        NetworkSentBytes = NonNegative(networkSentBytes);
        ProcessWorkingSetBytes = NonNegative(processWorkingSetBytes);
        SystemUptimeSeconds = NonNegative(systemUptimeSeconds);
        ExtendedMetricsJson = string.IsNullOrWhiteSpace(extendedMetricsJson) ? "{}" : extendedMetricsJson;
    }

    public long Id { get; private set; }
    public Guid MonitorTargetId { get; private set; }
    public DateTimeOffset CollectedAtUtc { get; private set; }
    public double CpuPercent { get; private set; }
    public long MemoryUsedBytes { get; private set; }
    public long MemoryTotalBytes { get; private set; }
    public long DiskUsedBytes { get; private set; }
    public long DiskTotalBytes { get; private set; }
    public long NetworkReceivedBytes { get; private set; }
    public long NetworkSentBytes { get; private set; }
    public long ProcessWorkingSetBytes { get; private set; }
    public long SystemUptimeSeconds { get; private set; }
    public string ExtendedMetricsJson { get; private set; } = "{}";

    private static long NonNegative(long value) => Math.Max(0, value);
}
