using AiDataGateway.Domain.DataSources;

namespace AiDataGateway.Application.DataSources;

public sealed record DataSourceUpsertRequest(
    string Key,
    string Name,
    DatabaseProvider Provider,
    string Host,
    int Port,
    string Database,
    string Username,
    string? Password,
    DataSourceAccessMode AccessMode,
    int MaxRows = 1_000,
    int CommandTimeoutSeconds = 30,
    bool Enabled = true,
    string[]? BlockedTables = null);

public sealed record DataSourceView(
    Guid Id,
    string Key,
    string Name,
    DatabaseProvider Provider,
    string Host,
    int Port,
    string Database,
    string Username,
    DataSourceAccessMode AccessMode,
    int MaxRows,
    int CommandTimeoutSeconds,
    bool Enabled,
    IReadOnlyList<string> BlockedTables,
    bool HasPassword,
    DateTimeOffset UpdatedAtUtc);
