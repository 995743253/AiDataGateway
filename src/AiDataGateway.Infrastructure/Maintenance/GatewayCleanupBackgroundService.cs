using AiDataGateway.Application.Maintenance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiDataGateway.Infrastructure.Maintenance;

internal sealed class GatewayCleanupBackgroundService(
    IServiceScopeFactory scopeFactory,
    MaintenanceScheduleSignal scheduleSignal,
    ILogger<GatewayCleanupBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Hosted services are started before the desktop window is shown. Always yield the
        // startup path so cleanup cannot delay WinForms/WebView initialization.
        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);

        var startupCleanupPending = true;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                MaintenanceSettingsView settings;
                await using (var scope = scopeFactory.CreateAsyncScope())
                {
                    settings = await scope.ServiceProvider.GetRequiredService<MaintenanceService>().GetAsync(stoppingToken);
                }
                if (!settings.CleanupEnabled)
                {
                    startupCleanupPending = false;
                    await scheduleSignal.WaitAsync(stoppingToken);
                    continue;
                }

                if (startupCleanupPending)
                {
                    await using var startupScope = scopeFactory.CreateAsyncScope();
                    await startupScope.ServiceProvider.GetRequiredService<MaintenanceService>()
                        .RunCleanupAsync("system-startup", stoppingToken);
                    startupCleanupPending = false;
                    continue;
                }

                var cleanupTime = TimeOnly.ParseExact(settings.CleanupTimeLocal, "HH:mm");
                var now = DateTime.Now;
                var nextRun = now.Date.Add(cleanupTime.ToTimeSpan());
                if (nextRun <= now) nextRun = nextRun.AddDays(1);

                var scheduleChanged = await scheduleSignal.WaitUntilAsync(nextRun - now, stoppingToken);
                if (scheduleChanged) continue;

                await using var cleanupScope = scopeFactory.CreateAsyncScope();
                await cleanupScope.ServiceProvider.GetRequiredService<MaintenanceService>()
                    .RunCleanupAsync("system-scheduler", stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Scheduled gateway cleanup failed.");
                try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }
}
