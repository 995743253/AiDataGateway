using AiDataGateway.Application.Abstractions;
using AiDataGateway.Domain.DataSources;

namespace AiDataGateway.Application.DataSources;

public sealed class DataSourceService(
    IDataSourceRepository repository,
    ICredentialProtector credentialProtector,
    IDatabaseAdapterFactory adapterFactory,
    IAuditWriter auditWriter)
{
    public async Task<IReadOnlyList<DataSourceView>> ListAsync(CancellationToken cancellationToken = default)
    {
        var items = await repository.ListAsync(cancellationToken);
        return items.Select(ToView).ToArray();
    }

    public async Task<DataSourceView> CreateAsync(DataSourceUpsertRequest request, string actor, CancellationToken cancellationToken = default)
    {
        if (await repository.FindByKeyAsync(request.Key, cancellationToken) is not null)
        {
            throw new InvalidOperationException($"Data source key '{request.Key}' already exists.");
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new ArgumentException("Password is required when creating a data source.", nameof(request));
        }

        var entity = new DataSourceDefinition(
            request.Key,
            request.Name,
            request.Provider,
            request.Host,
            request.Port,
            request.Database,
            request.Username,
            request.AccessMode);

        entity.Update(request.Key, request.Name, request.Provider, request.Host, request.Port, request.Database, request.Username,
            request.AccessMode, request.MaxRows, request.CommandTimeoutSeconds, request.Enabled);
        entity.SetProtectedPassword(credentialProtector.Protect(request.Password));

        await repository.AddAsync(entity, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        await auditWriter.WriteAsync(actor, "datasource.create", "success", entity.Id, entity.Key, cancellationToken);
        return ToView(entity);
    }

    public async Task<DataSourceView> UpdateAsync(Guid id, DataSourceUpsertRequest request, string actor, CancellationToken cancellationToken = default)
    {
        var entity = await repository.FindAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException("Data source was not found.");

        var sameKey = await repository.FindByKeyAsync(request.Key, cancellationToken);
        if (sameKey is not null && sameKey.Id != id)
        {
            throw new InvalidOperationException($"Data source key '{request.Key}' already exists.");
        }

        entity.Update(request.Key, request.Name, request.Provider, request.Host, request.Port, request.Database, request.Username,
            request.AccessMode, request.MaxRows, request.CommandTimeoutSeconds, request.Enabled);
        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            entity.SetProtectedPassword(credentialProtector.Protect(request.Password));
        }

        await repository.SaveChangesAsync(cancellationToken);
        await auditWriter.WriteAsync(actor, "datasource.update", "success", entity.Id, entity.Key, cancellationToken);
        return ToView(entity);
    }

    public async Task DeleteAsync(Guid id, string actor, CancellationToken cancellationToken = default)
    {
        var entity = await repository.FindAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException("Data source was not found.");
        await repository.DeleteAsync(entity, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        await auditWriter.WriteAsync(actor, "datasource.delete", "success", entity.Id, entity.Key, cancellationToken);
    }

    public async Task<ConnectionTestResult> TestAsync(Guid id, string actor, CancellationToken cancellationToken = default)
    {
        var entity = await repository.FindAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException("Data source was not found.");
        var connection = ToConnection(entity);
        var result = await adapterFactory.Get(entity.Provider).TestConnectionAsync(connection, cancellationToken);
        await auditWriter.WriteAsync(actor, "datasource.test", result.Success ? "success" : "failure", entity.Id, result.Message, cancellationToken);
        return result;
    }

    internal DatabaseConnection ToConnection(DataSourceDefinition entity) => new(
        entity.Host,
        entity.Port,
        entity.Database,
        entity.Username,
        credentialProtector.Unprotect(entity.ProtectedPassword),
        entity.CommandTimeoutSeconds);

    private static DataSourceView ToView(DataSourceDefinition entity) => new(
        entity.Id,
        entity.Key,
        entity.Name,
        entity.Provider,
        entity.Host,
        entity.Port,
        entity.Database,
        entity.Username,
        entity.AccessMode,
        entity.MaxRows,
        entity.CommandTimeoutSeconds,
        entity.Enabled,
        !string.IsNullOrWhiteSpace(entity.ProtectedPassword),
        entity.UpdatedAtUtc);
}
