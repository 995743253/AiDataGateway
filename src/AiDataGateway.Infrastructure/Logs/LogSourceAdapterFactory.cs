using AiDataGateway.Application.Abstractions;
using AiDataGateway.Domain.Logs;

namespace AiDataGateway.Infrastructure.Logs;

internal sealed class LogSourceAdapterFactory(IEnumerable<ILogSourceAdapter> adapters) : ILogSourceAdapterFactory
{
    private readonly IReadOnlyDictionary<LogSourceType, ILogSourceAdapter> _adapters =
        adapters.ToDictionary(item => item.Type);

    public ILogSourceAdapter Get(LogSourceType type) => _adapters.TryGetValue(type, out var adapter)
        ? adapter
        : throw new NotSupportedException($"Log source type '{type}' is not supported.");
}
