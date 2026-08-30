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
        await EnsureProjectAndLogTablesAsync(cancellationToken);
        await EnsureMonitoringTablesAsync(cancellationToken);
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

    private async Task EnsureMonitoringTablesAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "GatewayMonitorTargets" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_GatewayMonitorTargets" PRIMARY KEY,
                "Key" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "Type" TEXT NOT NULL,
                "IngestSecretHash" TEXT NOT NULL,
                "Enabled" INTEGER NOT NULL,
                "HostName" TEXT NOT NULL,
                "OsDescription" TEXT NOT NULL,
                "MetricSelection" TEXT NOT NULL DEFAULT 'cpu.percent,memory.percent,memory.used_bytes,disk.percent,disk.used_bytes,network.received_total_bytes,network.sent_total_bytes,process.working_set_bytes,system.uptime_seconds',
                "LastSeenAtUtc" TEXT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL
            )
            """, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_GatewayMonitorTargets_Key\" ON \"GatewayMonitorTargets\" (\"Key\")", cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "GatewayServerMetricSamples" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_GatewayServerMetricSamples" PRIMARY KEY AUTOINCREMENT,
                "MonitorTargetId" TEXT NOT NULL,
                "CollectedAtUtc" TEXT NOT NULL,
                "CpuPercent" REAL NOT NULL,
                "MemoryUsedBytes" INTEGER NOT NULL,
                "MemoryTotalBytes" INTEGER NOT NULL,
                "DiskUsedBytes" INTEGER NOT NULL,
                "DiskTotalBytes" INTEGER NOT NULL,
                "NetworkReceivedBytes" INTEGER NOT NULL,
                "NetworkSentBytes" INTEGER NOT NULL,
                "ProcessWorkingSetBytes" INTEGER NOT NULL,
                "SystemUptimeSeconds" INTEGER NOT NULL,
                "ExtendedMetricsJson" TEXT NOT NULL DEFAULT '{{}}',
                CONSTRAINT "FK_GatewayServerMetricSamples_GatewayMonitorTargets_MonitorTargetId" FOREIGN KEY ("MonitorTargetId") REFERENCES "GatewayMonitorTargets" ("Id") ON DELETE CASCADE
            )
            """, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_GatewayServerMetricSamples_MonitorTargetId_Id\" ON \"GatewayServerMetricSamples\" (\"MonitorTargetId\", \"Id\")", cancellationToken);

        await EnsureColumnAsync(
            "GatewayMonitorTargets",
            "MetricSelection",
            "ALTER TABLE \"GatewayMonitorTargets\" ADD COLUMN \"MetricSelection\" TEXT NOT NULL DEFAULT 'cpu.percent,memory.percent,memory.used_bytes,disk.percent,disk.used_bytes,network.received_total_bytes,network.sent_total_bytes,process.working_set_bytes,system.uptime_seconds'",
            cancellationToken);
        await EnsureColumnAsync(
            "GatewayServerMetricSamples",
            "ExtendedMetricsJson",
            "ALTER TABLE \"GatewayServerMetricSamples\" ADD COLUMN \"ExtendedMetricsJson\" TEXT NOT NULL DEFAULT '{{}}'",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "GatewayProjectMonitorTargets" (
                "ProjectId" TEXT NOT NULL,
                "MonitorTargetId" TEXT NOT NULL,
                CONSTRAINT "PK_GatewayProjectMonitorTargets" PRIMARY KEY ("ProjectId", "MonitorTargetId"),
                CONSTRAINT "FK_GatewayProjectMonitorTargets_GatewayProjects_ProjectId" FOREIGN KEY ("ProjectId") REFERENCES "GatewayProjects" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_GatewayProjectMonitorTargets_GatewayMonitorTargets_MonitorTargetId" FOREIGN KEY ("MonitorTargetId") REFERENCES "GatewayMonitorTargets" ("Id") ON DELETE CASCADE
            )
            """, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_GatewayProjectMonitorTargets_MonitorTargetId\" ON \"GatewayProjectMonitorTargets\" (\"MonitorTargetId\")", cancellationToken);

        if (!await dbContext.MonitorTargets.AnyAsync(item => item.Key == "local", cancellationToken))
        {
            await dbContext.MonitorTargets.AddAsync(new Domain.Monitoring.MonitorTargetDefinition(
                "local", "本机网关服务器", Domain.Monitoring.MonitorTargetType.Local), cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
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

    private async Task EnsureProjectAndLogTablesAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "GatewayProjects" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_GatewayProjects" PRIMARY KEY,
                "Code" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "Description" TEXT NOT NULL,
                "Enabled" INTEGER NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL
            )
            """, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_GatewayProjects_Code\" ON \"GatewayProjects\" (\"Code\")",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "GatewayProjectDataSources" (
                "ProjectId" TEXT NOT NULL,
                "DataSourceId" TEXT NOT NULL,
                CONSTRAINT "PK_GatewayProjectDataSources" PRIMARY KEY ("ProjectId", "DataSourceId"),
                CONSTRAINT "FK_GatewayProjectDataSources_GatewayProjects_ProjectId" FOREIGN KEY ("ProjectId") REFERENCES "GatewayProjects" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_GatewayProjectDataSources_GatewayDataSources_DataSourceId" FOREIGN KEY ("DataSourceId") REFERENCES "GatewayDataSources" ("Id") ON DELETE CASCADE
            )
            """, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_GatewayProjectDataSources_DataSourceId\" ON \"GatewayProjectDataSources\" (\"DataSourceId\")",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "GatewayLogSources" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_GatewayLogSources" PRIMARY KEY,
                "Key" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "Type" TEXT NOT NULL,
                "Endpoint" TEXT NOT NULL,
                "NLogTargetName" TEXT NOT NULL,
                "NLogLayout" TEXT NOT NULL,
                "ProtectedConfiguration" TEXT NOT NULL,
                "ProtectedApiKey" TEXT NOT NULL,
                "Enabled" INTEGER NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL
            )
            """, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_GatewayLogSources_Key\" ON \"GatewayLogSources\" (\"Key\")",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "GatewayProjectLogSources" (
                "ProjectId" TEXT NOT NULL,
                "LogSourceId" TEXT NOT NULL,
                CONSTRAINT "PK_GatewayProjectLogSources" PRIMARY KEY ("ProjectId", "LogSourceId"),
                CONSTRAINT "FK_GatewayProjectLogSources_GatewayProjects_ProjectId" FOREIGN KEY ("ProjectId") REFERENCES "GatewayProjects" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_GatewayProjectLogSources_GatewayLogSources_LogSourceId" FOREIGN KEY ("LogSourceId") REFERENCES "GatewayLogSources" ("Id") ON DELETE CASCADE
            )
            """, cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_GatewayProjectLogSources_LogSourceId\" ON \"GatewayProjectLogSources\" (\"LogSourceId\")",
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
