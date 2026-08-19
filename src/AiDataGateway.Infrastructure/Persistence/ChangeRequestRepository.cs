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

        return pending.OrderByDescending(item => item.CreatedAtUtc).ToList();
    }

    public Task<ChangeRequest?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.ChangeRequests.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public Task AddAsync(ChangeRequest request, CancellationToken cancellationToken = default) =>
        dbContext.ChangeRequests.AddAsync(request, cancellationToken).AsTask();

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => dbContext.SaveChangesAsync(cancellationToken);
}
