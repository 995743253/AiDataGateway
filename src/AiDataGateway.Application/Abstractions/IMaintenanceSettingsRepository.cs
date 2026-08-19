using AiDataGateway.Domain.Maintenance;

namespace AiDataGateway.Application.Abstractions;

public interface IMaintenanceSettingsRepository
{
    Task<MaintenanceSettings> GetAsync(CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
