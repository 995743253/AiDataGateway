using AiDataGateway.Application.Abstractions;
using AiDataGateway.Domain.Auditing;
using Microsoft.EntityFrameworkCore;

namespace AiDataGateway.Infrastructure.Persistence;

internal sealed class AuditLogReader(GatewayDbContext dbContext) : IAuditLogReader
{
    public async Task<IReadOnlyList<AuditEntry>> ListRecentAsync(int take = 200, CancellationToken cancellationToken = default)
    {
        var entries = await dbContext.AuditEntries.AsNoTracking().ToListAsync(cancellationToken);
        return entries
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(Math.Clamp(take, 1, 1_000))
            .ToList();
    }
}
