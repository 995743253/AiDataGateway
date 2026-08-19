namespace AiDataGateway.Application.Abstractions;

public interface IAuditWriter
{
    Task WriteAsync(string actor, string action, string outcome, Guid? dataSourceId = null, string? detail = null, CancellationToken cancellationToken = default);
}
