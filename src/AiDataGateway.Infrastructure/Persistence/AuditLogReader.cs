using AiDataGateway.Application.Abstractions;
using AiDataGateway.Domain.Auditing;
using Microsoft.EntityFrameworkCore;

namespace AiDataGateway.Infrastructure.Persistence;

internal sealed class AuditLogReader(GatewayDbContext dbContext) : IAuditLogReader
{
    public async Task<PagedResult<AuditEntry>> SearchAsync(
        string? keyword,
        string? action,
        string? outcome,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.AuditEntries.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(action))
        {
            var actionFilter = action.Trim();
            query = query.Where(item => item.Action == actionFilter);
        }
        if (!string.IsNullOrWhiteSpace(outcome))
        {
            var outcomeFilter = outcome.Trim();
            query = query.Where(item => item.Outcome == outcomeFilter);
        }
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var search = keyword.Trim();
            query = query.Where(item =>
                item.Actor.Contains(search) ||
                item.Action.Contains(search) ||
                item.Outcome.Contains(search) ||
                (item.Detail != null && item.Detail.Contains(search)));
        }

        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 200);
        var entries = await query.ToListAsync(cancellationToken);
        var ordered = entries
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToList();
        var items = ordered
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToList();
        return new PagedResult<AuditEntry>(items, ordered.Count, normalizedPage, normalizedPageSize);
    }
}
