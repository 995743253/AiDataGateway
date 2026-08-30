using AiDataGateway.Domain.Projects;

namespace AiDataGateway.Application.Abstractions;

public interface IProjectRepository
{
    Task<IReadOnlyList<ProjectDefinition>> ListAsync(CancellationToken cancellationToken = default);
    Task<ProjectDefinition?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProjectDefinition?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> ListDataSourceIdsAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> ListLogSourceIdsAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> ListMonitorTargetIdsAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task ReplaceDataSourcesAsync(Guid projectId, IEnumerable<Guid> dataSourceIds, CancellationToken cancellationToken = default);
    Task ReplaceLogSourcesAsync(Guid projectId, IEnumerable<Guid> logSourceIds, CancellationToken cancellationToken = default);
    Task ReplaceMonitorTargetsAsync(Guid projectId, IEnumerable<Guid> monitorTargetIds, CancellationToken cancellationToken = default);
    Task AddAsync(ProjectDefinition project, CancellationToken cancellationToken = default);
    Task DeleteAsync(ProjectDefinition project, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
