using AiDataGateway.Application.Abstractions;
using AiDataGateway.Domain.Projects;
using Microsoft.EntityFrameworkCore;

namespace AiDataGateway.Infrastructure.Persistence;

internal sealed class ProjectRepository(GatewayDbContext dbContext) : IProjectRepository
{
    public async Task<IReadOnlyList<ProjectDefinition>> ListAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Projects.OrderBy(item => item.Name).ToListAsync(cancellationToken);

    public Task<ProjectDefinition?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Projects.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public Task<ProjectDefinition?> FindByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        dbContext.Projects.SingleOrDefaultAsync(item => item.Code == code.Trim().ToLower(), cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListDataSourceIdsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        await dbContext.ProjectDataSources
            .Where(item => item.ProjectId == projectId)
            .Select(item => item.DataSourceId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListLogSourceIdsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        await dbContext.ProjectLogSources
            .Where(item => item.ProjectId == projectId)
            .Select(item => item.LogSourceId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListMonitorTargetIdsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        await dbContext.ProjectMonitorTargets.Where(item => item.ProjectId == projectId)
            .Select(item => item.MonitorTargetId).ToListAsync(cancellationToken);

    public async Task ReplaceDataSourcesAsync(Guid projectId, IEnumerable<Guid> dataSourceIds, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.ProjectDataSources
            .Where(item => item.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        dbContext.ProjectDataSources.RemoveRange(existing);
        var links = dataSourceIds.Distinct().Select(id => new ProjectDataSourceLink(projectId, id));
        await dbContext.ProjectDataSources.AddRangeAsync(links, cancellationToken);
    }

    public async Task ReplaceLogSourcesAsync(Guid projectId, IEnumerable<Guid> logSourceIds, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.ProjectLogSources
            .Where(item => item.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        dbContext.ProjectLogSources.RemoveRange(existing);
        var links = logSourceIds.Distinct().Select(id => new ProjectLogSourceLink(projectId, id));
        await dbContext.ProjectLogSources.AddRangeAsync(links, cancellationToken);
    }

    public async Task ReplaceMonitorTargetsAsync(Guid projectId, IEnumerable<Guid> monitorTargetIds, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.ProjectMonitorTargets.Where(item => item.ProjectId == projectId).ToListAsync(cancellationToken);
        dbContext.ProjectMonitorTargets.RemoveRange(existing);
        await dbContext.ProjectMonitorTargets.AddRangeAsync(
            monitorTargetIds.Distinct().Select(id => new ProjectMonitorTargetLink(projectId, id)), cancellationToken);
    }

    public Task AddAsync(ProjectDefinition project, CancellationToken cancellationToken = default) =>
        dbContext.Projects.AddAsync(project, cancellationToken).AsTask();

    public Task DeleteAsync(ProjectDefinition project, CancellationToken cancellationToken = default)
    {
        dbContext.Projects.Remove(project);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => dbContext.SaveChangesAsync(cancellationToken);
}
