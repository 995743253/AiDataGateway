using AiDataGateway.Domain.Logs;

namespace AiDataGateway.Application.Abstractions;

public sealed record LogSourceConnection(
    LogSourceType Type,
    string Endpoint,
    string NLogConfiguration,
    string NLogTargetName,
    string NLogLayout,
    string ApiKey);

public sealed record LogQueryOptions(
    string? Query = null,
    string? Level = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    string? SearchText = null,
    string? PropertyName = null,
    string? PropertyValue = null,
    int Page = 1,
    int PageSize = 100);

public sealed record StructuredLogEvent(
    string Id,
    DateTimeOffset? TimestampUtc,
    string? Level,
    string? Message,
    string? Exception,
    IReadOnlyDictionary<string, object?> Properties,
    string RawText,
    bool Incomplete = false,
    string? ParseWarning = null);

public sealed record LogQueryResult(
    IReadOnlyList<StructuredLogEvent> Items,
    int Page,
    int PageSize,
    int Total,
    bool IsPartial,
    string? Warning = null);

public sealed record LogSourceTestResult(bool Success, string Message);

public interface ILogSourceAdapter
{
    LogSourceType Type { get; }
    Task<LogSourceTestResult> TestAsync(LogSourceConnection connection, CancellationToken cancellationToken = default);
    Task<LogQueryResult> QueryAsync(LogSourceConnection connection, LogQueryOptions options, CancellationToken cancellationToken = default);
}

public interface ILogSourceAdapterFactory
{
    ILogSourceAdapter Get(LogSourceType type);
}
