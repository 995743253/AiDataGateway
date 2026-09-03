using AiDataGateway.Application.Abstractions;
using AiDataGateway.Domain.Toolbox;
using AiDataGateway.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AiDataGateway.Infrastructure.Toolbox;

internal sealed class ToolboxWebHookRepository(GatewayDbContext dbContext) : IToolboxWebHookRepository
{
    public async Task<IReadOnlyList<WebHookDefinition>> ListAsync(CancellationToken cancellationToken = default)
    {
        // SQLite cannot translate ORDER BY for DateTimeOffset values, so the
        // ordering happens in memory (same approach as ChangeRequestRepository).
        var hooks = await dbContext.ToolboxWebHooks
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return hooks.OrderByDescending(item => item.CreatedAtUtc).ToList();
    }

    public Task<WebHookDefinition?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.ToolboxWebHooks.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

    public Task<WebHookDefinition?> FindByTokenAsync(string token, CancellationToken cancellationToken = default) =>
        dbContext.ToolboxWebHooks.FirstOrDefaultAsync(item => item.Token == token, cancellationToken);

    public async Task AddAsync(WebHookDefinition hook, CancellationToken cancellationToken = default) =>
        await dbContext.ToolboxWebHooks.AddAsync(hook, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);

    public async Task DeleteAsync(WebHookDefinition hook, CancellationToken cancellationToken = default)
    {
        var deliveries = await dbContext.ToolboxWebHookDeliveries
            .Where(item => item.WebHookId == hook.Id)
            .ToListAsync(cancellationToken);
        dbContext.ToolboxWebHookDeliveries.RemoveRange(deliveries);
        dbContext.ToolboxWebHooks.Remove(hook);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> CountDeliveriesAsync(Guid webHookId, CancellationToken cancellationToken = default) =>
        await dbContext.ToolboxWebHookDeliveries.CountAsync(item => item.WebHookId == webHookId, cancellationToken);

    public async Task<IReadOnlyList<WebHookDelivery>> ListDeliveriesAsync(Guid webHookId, int limit, CancellationToken cancellationToken = default)
    {
        return await dbContext.ToolboxWebHookDeliveries
            .AsNoTracking()
            .Where(item => item.WebHookId == webHookId)
            .OrderByDescending(item => item.Id)
            .Take(Math.Clamp(limit, 1, 2000))
            .ToListAsync(cancellationToken);
    }

    public async Task AddDeliveryAsync(WebHookDelivery delivery, CancellationToken cancellationToken = default)
    {
        await dbContext.ToolboxWebHookDeliveries.AddAsync(delivery, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearDeliveriesAsync(Guid webHookId, CancellationToken cancellationToken = default) =>
        await dbContext.ToolboxWebHookDeliveries
            .Where(item => item.WebHookId == webHookId)
            .ExecuteDeleteAsync(cancellationToken);
}
