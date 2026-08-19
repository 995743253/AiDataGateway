using AiDataGateway.Application.Abstractions;
using AiDataGateway.Domain.DataSources;
using Microsoft.EntityFrameworkCore;

namespace AiDataGateway.Infrastructure.Persistence;

internal sealed class DataSourceRepository(GatewayDbContext dbContext) : IDataSourceRepository
{
    public async Task<IReadOnlyList<DataSourceDefinition>> ListAsync(CancellationToken cancellationToken = default) =>
        await dbContext.DataSources.OrderBy(item => item.Name).ToListAsync(cancellationToken);

    public Task<DataSourceDefinition?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.DataSources.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

    public Task<DataSourceDefinition?> FindByKeyAsync(string key, CancellationToken cancellationToken = default) =>
        dbContext.DataSources.SingleOrDefaultAsync(item => item.Key == key.ToLower(), cancellationToken);

    public Task AddAsync(DataSourceDefinition dataSource, CancellationToken cancellationToken = default) =>
        dbContext.DataSources.AddAsync(dataSource, cancellationToken).AsTask();

    public Task DeleteAsync(DataSourceDefinition dataSource, CancellationToken cancellationToken = default)
    {
        dbContext.DataSources.Remove(dataSource);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => dbContext.SaveChangesAsync(cancellationToken);
}
