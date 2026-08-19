using AiDataGateway.Application.Abstractions;
using AiDataGateway.Domain.Auditing;

namespace AiDataGateway.Infrastructure.Persistence;

internal sealed class AuditWriter(GatewayDbContext dbContext) : IAuditWriter
{
    public async Task WriteAsync(string actor, string action, string outcome, Guid? dataSourceId = null, string? detail = null, CancellationToken cancellationToken = default)
    {
        await dbContext.AuditEntries.AddAsync(new AuditEntry(actor, action, outcome, dataSourceId, detail), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
