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

    public async Task<PagedResult<ChangeRequest>> SearchAsync(
        ChangeStatus? status,
        string? keyword,
        Guid? dataSourceId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.ChangeRequests.AsNoTracking();
        if (status.HasValue && status is not ChangeStatus.Pending and not ChangeStatus.Expired)
        {
            query = query.Where(item => item.Status == status.Value);
        }
        else if (status is ChangeStatus.Pending or ChangeStatus.Expired)
        {
            query = query.Where(item => item.Status == ChangeStatus.Pending);
        }

        if (dataSourceId.HasValue)
        {
            query = query.Where(item => item.DataSourceId == dataSourceId.Value);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var search = keyword.Trim();
            query = query.Where(item =>
                item.Sql.Contains(search) ||
                item.RequestedBy.Contains(search) ||
                (item.ReviewedBy != null && item.ReviewedBy.Contains(search)) ||
                (item.ReviewComment != null && item.ReviewComment.Contains(search)) ||
                (item.ExecutionError != null && item.ExecutionError.Contains(search)));
        }

        var changes = await query.ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var filtered = changes.AsEnumerable();
        if (status == ChangeStatus.Pending)
        {
            filtered = filtered.Where(item => item.ExpiresAtUtc > now);
        }
        else if (status == ChangeStatus.Expired)
        {
            filtered = filtered.Where(item => item.ExpiresAtUtc <= now);
        }

        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 200);
        var ordered = filtered
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToList();
        var items = ordered
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToList();
        return new PagedResult<ChangeRequest>(items, ordered.Count, normalizedPage, normalizedPageSize);
    }

    public Task<ChangeRequest?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.ChangeRequests.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public Task AddAsync(ChangeRequest request, CancellationToken cancellationToken = default) =>
        dbContext.ChangeRequests.AddAsync(request, cancellationToken).AsTask();

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => dbContext.SaveChangesAsync(cancellationToken);
}
