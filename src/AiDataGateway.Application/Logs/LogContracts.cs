using AiDataGateway.Application.Abstractions;
using AiDataGateway.Domain.Logs;

namespace AiDataGateway.Application.Logs;

public sealed record LogSourceUpsertRequest(
    string Key,
    string Name,
    LogSourceType Type,
    string? Endpoint,
    string? NLogConfiguration,
    string? NLogTargetName,
    string? NLogLayout,
    string? ApiKey,
    bool Enabled = true,
    Guid[]? ProjectIds = null);

public sealed record LogSourceProjectReference(Guid Id, string Code, string Name, bool Enabled);

public sealed record LogSourceView(
    Guid Id,
    string Key,
    string Name,
    LogSourceType Type,
    string Endpoint,
    string NLogConfiguration,
    string NLogTargetName,
    string NLogLayout,
    bool HasApiKey,
    bool Enabled,
    IReadOnlyList<LogSourceProjectReference> Projects,
    DateTimeOffset UpdatedAtUtc);

public sealed record LogQueryRequest(
    Guid LogSourceId,
    string? Query = null,
    string? Level = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    string? SearchText = null,
    string? PropertyName = null,
    string? PropertyValue = null,
    int Page = 1,
    int PageSize = 100);

public sealed record ProjectLogQueryRequest(
    string ProjectCode,
    string? LogSourceKey = null,
    string? Query = null,
    string? Level = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    string? SearchText = null,
    string? PropertyName = null,
    string? PropertyValue = null,
    int Page = 1,
    int PageSize = 100);

public sealed record LogQueryView(
    Guid LogSourceId,
    string LogSourceKey,
    string LogSourceName,
    string ProjectCode,
    IReadOnlyList<StructuredLogEvent> Items,
    int Page,
    int PageSize,
    int Total,
    bool IsPartial,
    string? Warning);

public sealed record LogSqlProjectView(
    Guid Id,
    string Code,
    string Name,
    IReadOnlyList<LogSqlDataSourceView> DataSources);

public sealed record LogSqlDataSourceView(
    Guid Id,
    string Key,
    string Name,
    string Provider);

public sealed record LogSqlQueryRequest(
    Guid ProjectId,
    Guid DataSourceId,
    string Sql);
