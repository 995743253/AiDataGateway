namespace AiDataGateway.Application.Projects;

public sealed record ProjectUpsertRequest(
    string Code,
    string Name,
    string? Description,
    bool Enabled = true,
    Guid[]? DataSourceIds = null,
    Guid[]? LogSourceIds = null,
    Guid[]? MonitorTargetIds = null);

public sealed record ProjectDataSourceReference(
    Guid Id,
    string Key,
    string Name,
    string Provider,
    bool Enabled);

public sealed record ProjectLogSourceReference(
    Guid Id,
    string Key,
    string Name,
    string Type,
    bool Enabled);

public sealed record ProjectMonitorTargetReference(
    Guid Id,
    string Key,
    string Name,
    string Type,
    bool Enabled);

public sealed record ProjectView(
    Guid Id,
    string Code,
    string Name,
    string Description,
    bool Enabled,
    IReadOnlyList<ProjectDataSourceReference> DataSources,
    IReadOnlyList<ProjectLogSourceReference> LogSources,
    IReadOnlyList<ProjectMonitorTargetReference> MonitorTargets,
    DateTimeOffset UpdatedAtUtc);
