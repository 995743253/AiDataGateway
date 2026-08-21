using AiDataGateway.Application.Security;
using AiDataGateway.Domain.Maintenance;
using AiDataGateway.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace AiDataGateway.Infrastructure;

public sealed class GatewayDatabaseInitializer(
    GatewayDbContext dbContext,
    RoleManager<IdentityRole<Guid>> roleManager)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        await EnsureTableBlacklistColumnAsync(cancellationToken);
        await EnsureMaintenanceSettingsAsync(cancellationToken);
        foreach (var role in GatewayRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var result = await roleManager.CreateAsync(new IdentityRole<Guid>(role));
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Description)));
                }
            }
        }
    }

    private async Task EnsureMaintenanceSettingsAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "GatewayMaintenanceSettings" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_GatewayMaintenanceSettings" PRIMARY KEY,
                "CleanupEnabled" INTEGER NOT NULL,
                "RetentionDays" INTEGER NOT NULL,
                "CleanupTimeLocal" TEXT NOT NULL,
                "ApprovalExpirationMinutes" INTEGER NOT NULL DEFAULT 15,
                "LastCleanupAtUtc" TEXT NULL,
                "LastCleanupSummary" TEXT NULL,
                "UpdatedAtUtc" TEXT NOT NULL
            )
            """, cancellationToken);

        await EnsureColumnAsync(
            "GatewayMaintenanceSettings",
            "ApprovalExpirationMinutes",
            "ALTER TABLE \"GatewayMaintenanceSettings\" ADD COLUMN \"ApprovalExpirationMinutes\" INTEGER NOT NULL DEFAULT 15",
            cancellationToken);

        if (!await dbContext.MaintenanceSettings.AnyAsync(item => item.Id == MaintenanceSettings.SingletonId, cancellationToken))
        {
            await dbContext.MaintenanceSettings.AddAsync(new MaintenanceSettings(), cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task EnsureTableBlacklistColumnAsync(CancellationToken cancellationToken)
    {
        await EnsureColumnAsync(
            "GatewayDataSources",
            "TableBlacklist",
            "ALTER TABLE \"GatewayDataSources\" ADD COLUMN \"TableBlacklist\" TEXT NOT NULL DEFAULT ''",
            cancellationToken);
    }

    private async Task EnsureColumnAsync(string tableName, string columnName, string alterSql, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var hasColumn = false;
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = $"PRAGMA table_info(\"{tableName}\")";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                    {
                        hasColumn = true;
                        break;
                    }
                }
            }

            if (!hasColumn)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = alterSql;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }
}
