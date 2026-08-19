namespace AiDataGateway.Application.Abstractions;

public sealed record CleanupResult(int AuditLogsDeleted, int ApprovalRecordsDeleted, int LogFilesDeleted, DateTimeOffset CutoffUtc);

public interface IGatewayDataCleaner
{
    Task<CleanupResult> CleanupAsync(int retentionDays, CancellationToken cancellationToken = default);
}
