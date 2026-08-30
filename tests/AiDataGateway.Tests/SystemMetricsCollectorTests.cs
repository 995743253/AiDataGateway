using AiDataGateway.Monitoring;

namespace AiDataGateway.Tests;

public sealed class SystemMetricsCollectorTests
{
    [Fact]
    public void Catalog_exposes_required_and_optional_metrics()
    {
        Assert.True(MetricCatalog.All.Count >= 20);
        Assert.All(MetricCatalog.RequiredKeys, key => Assert.Contains(key, MetricCatalog.DefaultKeys));

        var normalized = MetricCatalog.NormalizeSelection(["process.thread_count", "unknown.metric"]);
        Assert.Contains("process.thread_count", normalized);
        Assert.DoesNotContain("unknown.metric", normalized);
        Assert.All(MetricCatalog.RequiredKeys, key => Assert.Contains(key, normalized));
    }

    [Fact]
    public void Collector_only_returns_requested_extended_metrics()
    {
        var snapshot = new SystemMetricsCollector().Collect([.. MetricCatalog.RequiredKeys, "system.process_count"]);

        Assert.True(snapshot.MemoryTotalBytes >= snapshot.MemoryUsedBytes);
        Assert.True(snapshot.DiskTotalBytes >= snapshot.DiskUsedBytes);
        Assert.Contains("system.process_count", snapshot.ExtendedMetrics.Keys);
        Assert.DoesNotContain("gc.heap_size_bytes", snapshot.ExtendedMetrics.Keys);
    }
}
