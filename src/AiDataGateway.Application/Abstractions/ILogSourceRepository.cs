using AiDataGateway.Domain.Logs;

namespace AiDataGateway.Application.Abstractions;

public interface ILogSourceRepository
{
    Task<IReadOnlyList<LogSourceDefinition>> ListAsync(CancellationToken cancellationToken = default);
    Task<LogSourceDefinition?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<LogSourceDefinition?> FindByKeyAsync(string key, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> ListProjectIdsAsync(Guid logSourceId, CancellationToken cancellationToken = default);
    Task ReplaceProjectsAsync(Guid logSourceId, IEnumerable<Guid> projectIds, CancellationToken cancellationToken = default);
    Task AddAsync(LogSourceDefinition source, CancellationToken cancellationToken = default);
    Task DeleteAsync(LogSourceDefinition source, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
