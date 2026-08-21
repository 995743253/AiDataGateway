using AiDataGateway.Application.Abstractions;

namespace AiDataGateway.Application.Maintenance;

public sealed class MaintenanceService(
    IMaintenanceSettingsRepository repository,
    IGatewayDataCleaner dataCleaner,
    IMaintenanceScheduleNotifier scheduleNotifier,
    IAuditWriter auditWriter)
{
    private static readonly SemaphoreSlim CleanupLock = new(1, 1);

    public async Task<MaintenanceSettingsView> GetAsync(CancellationToken cancellationToken = default) =>
        ToView(await repository.GetAsync(cancellationToken));

    public async Task<MaintenanceSettingsView> UpdateAsync(
        UpdateMaintenanceSettingsRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var settings = await repository.GetAsync(cancellationToken);
        settings.Update(request.CleanupEnabled, request.RetentionDays, request.CleanupTimeLocal, request.ApprovalExpirationMinutes);
        await repository.SaveChangesAsync(cancellationToken);
        scheduleNotifier.NotifyScheduleChanged();
        await auditWriter.WriteAsync(actor, "settings.maintenance.update", "success",
            detail: $"enabled={settings.CleanupEnabled};retentionDays={settings.RetentionDays};cleanupTime={settings.CleanupTimeLocal};approvalExpirationMinutes={settings.ApprovalExpirationMinutes}",
            cancellationToken: cancellationToken);
        return ToView(settings);
    }

    public async Task<CleanupResult> RunCleanupAsync(string actor, CancellationToken cancellationToken = default)
    {
        await CleanupLock.WaitAsync(cancellationToken);
        try
        {
            var settings = await repository.GetAsync(cancellationToken);
            var result = await dataCleaner.CleanupAsync(settings.RetentionDays, cancellationToken);
            var summary = $"审计日志 {result.AuditLogsDeleted} 条，审批记录 {result.ApprovalRecordsDeleted} 条，日志文件 {result.LogFilesDeleted} 个";
            settings.MarkCleanup(DateTimeOffset.UtcNow, summary);
            await repository.SaveChangesAsync(cancellationToken);
            await auditWriter.WriteAsync(actor, "maintenance.cleanup", "success", detail: summary, cancellationToken: cancellationToken);
            return result;
        }
        finally
        {
            CleanupLock.Release();
        }
    }

    private static MaintenanceSettingsView ToView(Domain.Maintenance.MaintenanceSettings settings) => new(
        settings.CleanupEnabled,
        settings.RetentionDays,
        settings.CleanupTimeLocal,
        settings.ApprovalExpirationMinutes,
        settings.LastCleanupAtUtc,
        settings.LastCleanupSummary,
        settings.UpdatedAtUtc);
}
