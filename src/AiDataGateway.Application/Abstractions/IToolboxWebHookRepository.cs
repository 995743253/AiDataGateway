using AiDataGateway.Domain.Toolbox;

namespace AiDataGateway.Application.Abstractions;

public interface IToolboxWebHookRepository
{
    Task<IReadOnlyList<WebHookDefinition>> ListAsync(CancellationToken cancellationToken = default);
    Task<WebHookDefinition?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WebHookDefinition?> FindByTokenAsync(string token, CancellationToken cancellationToken = default);
    Task AddAsync(WebHookDefinition hook, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
    Task DeleteAsync(WebHookDefinition hook, CancellationToken cancellationToken = default);
    Task<int> CountDeliveriesAsync(Guid webHookId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WebHookDelivery>> ListDeliveriesAsync(Guid webHookId, int limit, CancellationToken cancellationToken = default);
    Task AddDeliveryAsync(WebHookDelivery delivery, CancellationToken cancellationToken = default);
    Task ClearDeliveriesAsync(Guid webHookId, CancellationToken cancellationToken = default);
}
