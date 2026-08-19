using AiDataGateway.Domain.Approvals;

namespace AiDataGateway.Application.Abstractions;

public interface IChangeRequestRepository
{
    Task<IReadOnlyList<ChangeRequest>> ListPendingAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChangeRequest>> ListAsync(ChangeStatus? status = null, int take = 200, CancellationToken cancellationToken = default);
    Task<ChangeRequest?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(ChangeRequest request, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
