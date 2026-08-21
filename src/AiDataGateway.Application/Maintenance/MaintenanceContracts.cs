namespace AiDataGateway.Application.Maintenance;

public sealed record UpdateMaintenanceSettingsRequest(
    bool CleanupEnabled,
    int RetentionDays,
    string CleanupTimeLocal,
    int ApprovalExpirationMinutes = 15);

public sealed record MaintenanceSettingsView(
    bool CleanupEnabled,
    int RetentionDays,
    string CleanupTimeLocal,
    int ApprovalExpirationMinutes,
    DateTimeOffset? LastCleanupAtUtc,
    string? LastCleanupSummary,
    DateTimeOffset UpdatedAtUtc);
