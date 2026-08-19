using AiDataGateway.Application.Abstractions;
using AiDataGateway.Domain.Maintenance;
using Microsoft.EntityFrameworkCore;

namespace AiDataGateway.Infrastructure.Persistence;

internal sealed class MaintenanceSettingsRepository(GatewayDbContext dbContext) : IMaintenanceSettingsRepository
{
    public async Task<MaintenanceSettings> GetAsync(CancellationToken cancellationToken = default) =>
        await dbContext.MaintenanceSettings.SingleAsync(item => item.Id == MaintenanceSettings.SingletonId, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => dbContext.SaveChangesAsync(cancellationToken);
}
