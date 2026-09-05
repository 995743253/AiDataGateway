namespace AiDataGateway.Domain.Maintenance;

public sealed class MaintenanceSettings
{
    public const int SingletonId = 1;

    private MaintenanceSettings()
    {
    }

    public MaintenanceSettings(
        bool cleanupEnabled = true,
        int retentionDays = 3,
        string cleanupTimeLocal = "03:00",
        int approvalExpirationMinutes = 15)
    {
        Id = SingletonId;
        Update(cleanupEnabled, retentionDays, cleanupTimeLocal, approvalExpirationMinutes);
    }

    public int Id { get; private set; }
    public bool CleanupEnabled { get; private set; }
    public int RetentionDays { get; private set; }
    public string CleanupTimeLocal { get; private set; } = "03:00";
    public int ApprovalExpirationMinutes { get; private set; }
    public string ProtectedAdminResetPassword { get; private set; } = string.Empty;
    public DateTimeOffset? LastCleanupAtUtc { get; private set; }
    public string? LastCleanupSummary { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void Update(bool cleanupEnabled, int retentionDays, string cleanupTimeLocal, int approvalExpirationMinutes)
    {
        if (retentionDays is < 1 or > 3_650)
        {
            throw new ArgumentOutOfRangeException(nameof(retentionDays), "Retention days must be between 1 and 3650.");
        }
        if (!TimeOnly.TryParseExact(cleanupTimeLocal, "HH:mm", out _))
        {
            throw new ArgumentException("Cleanup time must use HH:mm format.", nameof(cleanupTimeLocal));
        }
        if (approvalExpirationMinutes is < 1 or > 10_080)
        {
            throw new ArgumentOutOfRangeException(nameof(approvalExpirationMinutes), "Approval expiration must be between 1 and 10080 minutes.");
        }

        CleanupEnabled = cleanupEnabled;
        RetentionDays = retentionDays;
        CleanupTimeLocal = cleanupTimeLocal;
        ApprovalExpirationMinutes = approvalExpirationMinutes;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void MarkCleanup(DateTimeOffset completedAtUtc, string summary)
    {
        LastCleanupAtUtc = completedAtUtc;
        LastCleanupSummary = summary;
        UpdatedAtUtc = completedAtUtc;
    }

    public void SetProtectedAdminResetPassword(string protectedPassword)
    {
        ProtectedAdminResetPassword = protectedPassword ?? throw new ArgumentNullException(nameof(protectedPassword));
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}
