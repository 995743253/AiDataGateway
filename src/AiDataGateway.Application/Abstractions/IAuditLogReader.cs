using AiDataGateway.Domain.Auditing;

namespace AiDataGateway.Application.Abstractions;

public interface IAuditLogReader
{
    Task<IReadOnlyList<AuditEntry>> ListRecentAsync(int take = 200, CancellationToken cancellationToken = default);
}
