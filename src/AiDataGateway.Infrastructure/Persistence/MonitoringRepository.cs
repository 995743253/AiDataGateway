using AiDataGateway.Application.Abstractions;
using AiDataGateway.Domain.Monitoring;
using Microsoft.EntityFrameworkCore;

namespace AiDataGateway.Infrastructure.Persistence;

internal sealed class MonitoringRepository(GatewayDbContext dbContext) : IMonitoringRepository
{
    public async Task<IReadOnlyList<MonitorTargetDefinition>> ListTargetsAsync(CancellationToken cancellationToken = default) =>
        await dbContext.MonitorTargets.OrderBy(item => item.Type).ThenBy(item => item.Name).ToListAsync(cancellationToken);

    public Task<MonitorTargetDefinition?> FindTargetAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.MonitorTargets.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public Task<MonitorTargetDefinition?> FindTargetByKeyAsync(string key, CancellationToken cancellationToken = default) =>
        dbContext.MonitorTargets.SingleOrDefaultAsync(item => item.Key == key.Trim().ToLower(), cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListProjectIdsAsync(Guid targetId, CancellationToken cancellationToken = default) =>
        await dbContext.ProjectMonitorTargets.Where(item => item.MonitorTargetId == targetId).Select(item => item.ProjectId).ToListAsync(cancellationToken);

    public async Task ReplaceProjectsAsync(Guid targetId, IEnumerable<Guid> projectIds, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.ProjectMonitorTargets.Where(item => item.MonitorTargetId == targetId).ToListAsync(cancellationToken);
        dbContext.ProjectMonitorTargets.RemoveRange(existing);
        await dbContext.ProjectMonitorTargets.AddRangeAsync(
            projectIds.Distinct().Select(projectId => new Domain.Projects.ProjectMonitorTargetLink(projectId, targetId)), cancellationToken);
    }

    public Task AddTargetAsync(MonitorTargetDefinition target, CancellationToken cancellationToken = default) =>
        dbContext.MonitorTargets.AddAsync(target, cancellationToken).AsTask();

    public Task DeleteTargetAsync(MonitorTargetDefinition target, CancellationToken cancellationToken = default)
    {
        dbContext.MonitorTargets.Remove(target);
        return Task.CompletedTask;
    }

    public Task AddSampleAsync(ServerMetricSample sample, CancellationToken cancellationToken = default) =>
        dbContext.ServerMetricSamples.AddAsync(sample, cancellationToken).AsTask();

    public Task<ServerMetricSample?> LatestSampleAsync(Guid targetId, CancellationToken cancellationToken = default) =>
        dbContext.ServerMetricSamples.Where(item => item.MonitorTargetId == targetId).OrderByDescending(item => item.Id).FirstOrDefaultAsync(cancellationToken);

    public async Task<(IReadOnlyList<ServerMetricSample> Items, int Total)> QuerySamplesAsync(
        Guid targetId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var targetSamples = await dbContext.ServerMetricSamples.AsNoTracking()
            .Where(item => item.MonitorTargetId == targetId)
            .OrderByDescending(item => item.Id)
            .ToListAsync(cancellationToken);
        var filtered = targetSamples.Where(item => (!fromUtc.HasValue || item.CollectedAtUtc >= fromUtc.Value) &&
                                                    (!toUtc.HasValue || item.CollectedAtUtc <= toUtc.Value));
        var total = filtered.Count();
        var items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        return (items, total);
    }

    public async Task<(IReadOnlyList<ServerMetricSample> Items, int Total)> QueryTrendSamplesAsync(
        Guid targetId, DateTimeOffset fromUtc, DateTimeOffset toUtc, int maxPoints, CancellationToken cancellationToken = default)
    {
        var targetSamples = await dbContext.ServerMetricSamples.AsNoTracking()
            .Where(item => item.MonitorTargetId == targetId)
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken);
        var filtered = targetSamples.Where(item => item.CollectedAtUtc >= fromUtc && item.CollectedAtUtc <= toUtc).ToArray();
        if (filtered.Length <= maxPoints)
        {
            return (filtered, filtered.Length);
        }

        var items = Enumerable.Range(0, maxPoints)
            .Select(index => filtered[(int)Math.Round(index * (filtered.Length - 1d) / (maxPoints - 1d))])
            .DistinctBy(item => item.Id)
            .ToArray();
        return (items, filtered.Length);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => dbContext.SaveChangesAsync(cancellationToken);
}
