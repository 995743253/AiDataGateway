using AiDataGateway.Application.Monitoring;
using AiDataGateway.Monitoring;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiDataGateway.Infrastructure.Monitoring;

internal sealed class LocalMetricsBackgroundService(
    IServiceScopeFactory scopeFactory,
    SystemMetricsCollector collector,
    ILogger<LocalMetricsBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var monitoring = scope.ServiceProvider.GetRequiredService<MonitoringService>();
                var metricKeys = await monitoring.GetLocalMetricKeysAsync(stoppingToken);
                var snapshot = collector.Collect(metricKeys);
                await monitoring.RecordLocalAsync(new MetricIngestRequest(
                    snapshot.CollectedAtUtc, snapshot.HostName, snapshot.OsDescription, snapshot.CpuPercent,
                    snapshot.MemoryUsedBytes, snapshot.MemoryTotalBytes, snapshot.DiskUsedBytes, snapshot.DiskTotalBytes,
                    snapshot.NetworkReceivedBytes, snapshot.NetworkSentBytes, snapshot.ProcessWorkingSetBytes, snapshot.SystemUptimeSeconds,
                    snapshot.ExtendedMetrics), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Local metrics collection failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
