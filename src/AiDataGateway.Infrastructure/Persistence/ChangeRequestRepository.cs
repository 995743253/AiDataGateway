using AiDataGateway.Application.Abstractions;
using AiDataGateway.Domain.Approvals;
using Microsoft.EntityFrameworkCore;

namespace AiDataGateway.Infrastructure.Persistence;

internal sealed class ChangeRequestRepository(GatewayDbContext dbContext) : IChangeRequestRepository
{
    public async Task<IReadOnlyList<ChangeRequest>> ListPendingAsync(CancellationToken cancellationToken = default)
    {
        // SQLite can persist DateTimeOffset values but cannot translate ORDER BY for them.
        // The pending approval set is intentionally small, so filter in SQLite and order in memory.
        var pending = await dbContext.ChangeRequests
            .AsNoTracking()
            .Where(item => item.Status == ChangeStatus.Pending)
            .ToListAsync(cancellationToken);

        return pending
            .Where(item => item.ExpiresAtUtc > DateTimeOffset.UtcNow)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToList();
    }

    public async Task<IReadOnlyList<ChangeRequest>> ListAsync(ChangeStatus? status = null, int take = 200, CancellationToken cancellationToken = default)
    {
        var query = dbContext.ChangeRequests.AsNoTracking();
        if (status.HasValue)
        {
            query = query.Where(item => item.Status == status.Value);
        }

        var changes = await query.ToListAsync(cancellationToken);
        return changes
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(Math.Clamp(take, 1, 1_000))
            .ToList();
    }

    public Task<ChangeRequest?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.ChangeRequests.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public Task AddAsync(ChangeRequest request, CancellationToken cancellationToken = default) =>
        dbContext.ChangeRequests.AddAsync(request, cancellationToken).AsTask();

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => dbContext.SaveChangesAsync(cancellationToken);
}
