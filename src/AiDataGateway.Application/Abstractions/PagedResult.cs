namespace AiDataGateway.Application.Abstractions;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
