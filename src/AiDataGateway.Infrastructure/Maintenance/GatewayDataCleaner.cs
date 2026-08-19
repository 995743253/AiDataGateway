using AiDataGateway.Application.Abstractions;
using AiDataGateway.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AiDataGateway.Infrastructure.Maintenance;

internal sealed class GatewayDataCleaner(GatewayDbContext dbContext, IOptions<GatewayStorageOptions> storageOptions) : IGatewayDataCleaner
{
    public async Task<CleanupResult> CleanupAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(retentionDays, 1, 3_650));
        var auditLogsDeleted = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM \"GatewayAuditEntries\" WHERE \"CreatedAtUtc\" < {cutoff}", cancellationToken);
        var approvalRecordsDeleted = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM \"GatewayChangeRequests\" WHERE \"CreatedAtUtc\" < {cutoff}", cancellationToken);

        var logFilesDeleted = 0;
        var logsPath = Path.Combine(storageOptions.Value.BasePath, "logs");
        if (Directory.Exists(logsPath))
        {
            foreach (var file in Directory.EnumerateFiles(logsPath, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.GetLastWriteTimeUtc(file) >= cutoff.UtcDateTime) continue;
                File.Delete(file);
                logFilesDeleted++;
            }
        }

        return new CleanupResult(auditLogsDeleted, approvalRecordsDeleted, logFilesDeleted, cutoff);
    }
}
