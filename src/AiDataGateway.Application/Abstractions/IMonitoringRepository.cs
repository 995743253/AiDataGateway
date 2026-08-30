using AiDataGateway.Domain.Monitoring;

namespace AiDataGateway.Application.Abstractions;

public interface IMonitoringRepository
{
    Task<IReadOnlyList<MonitorTargetDefinition>> ListTargetsAsync(CancellationToken cancellationToken = default);
    Task<MonitorTargetDefinition?> FindTargetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<MonitorTargetDefinition?> FindTargetByKeyAsync(string key, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> ListProjectIdsAsync(Guid targetId, CancellationToken cancellationToken = default);
    Task ReplaceProjectsAsync(Guid targetId, IEnumerable<Guid> projectIds, CancellationToken cancellationToken = default);
    Task AddTargetAsync(MonitorTargetDefinition target, CancellationToken cancellationToken = default);
    Task DeleteTargetAsync(MonitorTargetDefinition target, CancellationToken cancellationToken = default);
    Task AddSampleAsync(ServerMetricSample sample, CancellationToken cancellationToken = default);
    Task<ServerMetricSample?> LatestSampleAsync(Guid targetId, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<ServerMetricSample> Items, int Total)> QuerySamplesAsync(
        Guid targetId,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<ServerMetricSample> Items, int Total)> QueryTrendSamplesAsync(
        Guid targetId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int maxPoints,
        CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
