namespace AiDataGateway.Api.Contracts;

public sealed record ValidateSqlRequest(string Sql);
public sealed record ExecuteQueryRequest(Guid DataSourceId, string Sql);
public sealed record SubmitChangeRequest(Guid DataSourceId, string Sql);
public sealed record ReviewChangeRequest(bool Approved, string? Comment);
