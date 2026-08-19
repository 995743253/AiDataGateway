namespace AiDataGateway.Application.Maintenance;

public sealed record UpdateMaintenanceSettingsRequest(bool CleanupEnabled, int RetentionDays, string CleanupTimeLocal);

public sealed record MaintenanceSettingsView(
    bool CleanupEnabled,
    int RetentionDays,
    string CleanupTimeLocal,
    DateTimeOffset? LastCleanupAtUtc,
    string? LastCleanupSummary,
    DateTimeOffset UpdatedAtUtc);
