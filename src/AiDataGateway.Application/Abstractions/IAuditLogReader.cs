using AiDataGateway.Domain.Auditing;

namespace AiDataGateway.Application.Abstractions;

public interface IAuditLogReader
{
    Task<PagedResult<AuditEntry>> SearchAsync(string? keyword, string? action, string? outcome, int page, int pageSize, CancellationToken cancellationToken = default);
}
