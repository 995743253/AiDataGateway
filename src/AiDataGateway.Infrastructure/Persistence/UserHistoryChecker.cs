using AiDataGateway.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AiDataGateway.Infrastructure.Persistence;

internal sealed class UserHistoryChecker(GatewayDbContext dbContext) : IUserHistoryChecker
{
    public async Task<bool> HasHistoryAsync(string userName, CancellationToken cancellationToken = default)
    {
        return await dbContext.AuditEntries.AnyAsync(item => item.Actor == userName, cancellationToken) ||
               await dbContext.ChangeRequests.AnyAsync(item =>
                   item.RequestedBy == userName || item.ReviewedBy == userName, cancellationToken);
    }
}
