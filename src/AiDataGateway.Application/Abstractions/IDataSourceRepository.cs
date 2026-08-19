using AiDataGateway.Domain.DataSources;

namespace AiDataGateway.Application.Abstractions;

public interface IDataSourceRepository
{
    Task<IReadOnlyList<DataSourceDefinition>> ListAsync(CancellationToken cancellationToken = default);
    Task<DataSourceDefinition?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DataSourceDefinition?> FindByKeyAsync(string key, CancellationToken cancellationToken = default);
    Task AddAsync(DataSourceDefinition dataSource, CancellationToken cancellationToken = default);
    Task DeleteAsync(DataSourceDefinition dataSource, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
