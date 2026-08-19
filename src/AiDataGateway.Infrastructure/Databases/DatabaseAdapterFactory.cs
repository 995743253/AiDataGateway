using AiDataGateway.Application.Abstractions;
using AiDataGateway.Domain.DataSources;

namespace AiDataGateway.Infrastructure.Databases;

internal sealed class DatabaseAdapterFactory(IEnumerable<IDatabaseAdapter> adapters) : IDatabaseAdapterFactory
{
    private readonly IReadOnlyDictionary<DatabaseProvider, IDatabaseAdapter> _adapters = adapters.ToDictionary(item => item.Provider);

    public IDatabaseAdapter Get(DatabaseProvider provider) =>
        _adapters.TryGetValue(provider, out var adapter)
            ? adapter
            : throw new NotSupportedException($"Database provider '{provider}' is not registered.");
}
