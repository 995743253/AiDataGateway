using AiDataGateway.Application.Abstractions;
using AiDataGateway.Domain.Logs;
using Microsoft.EntityFrameworkCore;

namespace AiDataGateway.Infrastructure.Persistence;

internal sealed class LogSourceRepository(GatewayDbContext dbContext) : ILogSourceRepository
{
    public async Task<IReadOnlyList<LogSourceDefinition>> ListAsync(CancellationToken cancellationToken = default) =>
        await dbContext.LogSources.OrderBy(item => item.Name).ToListAsync(cancellationToken);

    public Task<LogSourceDefinition?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.LogSources.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public Task<LogSourceDefinition?> FindByKeyAsync(string key, CancellationToken cancellationToken = default) =>
        dbContext.LogSources.SingleOrDefaultAsync(item => item.Key == key.Trim().ToLower(), cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListProjectIdsAsync(Guid logSourceId, CancellationToken cancellationToken = default) =>
        await dbContext.ProjectLogSources.Where(item => item.LogSourceId == logSourceId)
            .Select(item => item.ProjectId)
            .ToListAsync(cancellationToken);

    public async Task ReplaceProjectsAsync(Guid logSourceId, IEnumerable<Guid> projectIds, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.ProjectLogSources.Where(item => item.LogSourceId == logSourceId)
            .ToListAsync(cancellationToken);
        dbContext.ProjectLogSources.RemoveRange(existing);
        await dbContext.ProjectLogSources.AddRangeAsync(
            projectIds.Distinct().Select(projectId => new Domain.Projects.ProjectLogSourceLink(projectId, logSourceId)),
            cancellationToken);
    }

    public Task AddAsync(LogSourceDefinition source, CancellationToken cancellationToken = default) =>
        dbContext.LogSources.AddAsync(source, cancellationToken).AsTask();

    public Task DeleteAsync(LogSourceDefinition source, CancellationToken cancellationToken = default)
    {
        dbContext.LogSources.Remove(source);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => dbContext.SaveChangesAsync(cancellationToken);
}
