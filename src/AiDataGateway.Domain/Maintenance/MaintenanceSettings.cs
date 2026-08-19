namespace AiDataGateway.Domain.Maintenance;

public sealed class MaintenanceSettings
{
    public const int SingletonId = 1;

    private MaintenanceSettings()
    {
    }

    public MaintenanceSettings(bool cleanupEnabled = true, int retentionDays = 3, string cleanupTimeLocal = "03:00")
    {
        Id = SingletonId;
        Update(cleanupEnabled, retentionDays, cleanupTimeLocal);
    }

    public int Id { get; private set; }
    public bool CleanupEnabled { get; private set; }
    public int RetentionDays { get; private set; }
    public string CleanupTimeLocal { get; private set; } = "03:00";
    public DateTimeOffset? LastCleanupAtUtc { get; private set; }
    public string? LastCleanupSummary { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void Update(bool cleanupEnabled, int retentionDays, string cleanupTimeLocal)
    {
        if (retentionDays is < 1 or > 3_650)
        {
            throw new ArgumentOutOfRangeException(nameof(retentionDays), "Retention days must be between 1 and 3650.");
        }
        if (!TimeOnly.TryParseExact(cleanupTimeLocal, "HH:mm", out _))
        {
            throw new ArgumentException("Cleanup time must use HH:mm format.", nameof(cleanupTimeLocal));
        }

        CleanupEnabled = cleanupEnabled;
        RetentionDays = retentionDays;
        CleanupTimeLocal = cleanupTimeLocal;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void MarkCleanup(DateTimeOffset completedAtUtc, string summary)
    {
        LastCleanupAtUtc = completedAtUtc;
        LastCleanupSummary = summary;
        UpdatedAtUtc = completedAtUtc;
    }
}
