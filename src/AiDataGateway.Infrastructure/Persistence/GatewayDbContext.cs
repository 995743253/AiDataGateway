using AiDataGateway.Domain.Approvals;
using AiDataGateway.Domain.Auditing;
using AiDataGateway.Domain.DataSources;
using AiDataGateway.Domain.Maintenance;
using AiDataGateway.Domain.Logs;
using AiDataGateway.Domain.Projects;
using AiDataGateway.Domain.Monitoring;
using AiDataGateway.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AiDataGateway.Infrastructure.Persistence;

public sealed class GatewayDbContext(DbContextOptions<GatewayDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<DataSourceDefinition> DataSources => Set<DataSourceDefinition>();
    public DbSet<ChangeRequest> ChangeRequests => Set<ChangeRequest>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<MaintenanceSettings> MaintenanceSettings => Set<MaintenanceSettings>();
    public DbSet<ProjectDefinition> Projects => Set<ProjectDefinition>();
    public DbSet<ProjectDataSourceLink> ProjectDataSources => Set<ProjectDataSourceLink>();
    public DbSet<ProjectLogSourceLink> ProjectLogSources => Set<ProjectLogSourceLink>();
    public DbSet<LogSourceDefinition> LogSources => Set<LogSourceDefinition>();
    public DbSet<MonitorTargetDefinition> MonitorTargets => Set<MonitorTargetDefinition>();
    public DbSet<ServerMetricSample> ServerMetricSamples => Set<ServerMetricSample>();
    public DbSet<ProjectMonitorTargetLink> ProjectMonitorTargets => Set<ProjectMonitorTargetLink>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<DataSourceDefinition>(entity =>
        {
            entity.ToTable("GatewayDataSources");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.Key).IsUnique();
            entity.Property(item => item.Key).HasMaxLength(100).IsRequired();
            entity.Property(item => item.Name).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Host).HasMaxLength(300).IsRequired();
            entity.Property(item => item.Database).HasMaxLength(300).IsRequired();
            entity.Property(item => item.Username).HasMaxLength(200).IsRequired();
            entity.Property(item => item.ProtectedPassword).IsRequired();
            entity.Property(item => item.TableBlacklist).HasDefaultValue(string.Empty).IsRequired();
            entity.Property(item => item.Provider).HasConversion<string>().HasMaxLength(50);
            entity.Property(item => item.AccessMode).HasConversion<string>().HasMaxLength(50);
        });

        builder.Entity<ChangeRequest>(entity =>
        {
            entity.ToTable("GatewayChangeRequests");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Sql).IsRequired();
            entity.Property(item => item.RequestedBy).HasMaxLength(200).IsRequired();
            entity.Property(item => item.ReviewedBy).HasMaxLength(200);
            entity.Property(item => item.RiskLevel).HasConversion<string>().HasMaxLength(30);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(30);
            entity.HasIndex(item => new { item.Status, item.CreatedAtUtc });
        });

        builder.Entity<AuditEntry>(entity =>
        {
            entity.ToTable("GatewayAuditEntries");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Actor).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Action).HasMaxLength(100).IsRequired();
            entity.Property(item => item.Outcome).HasMaxLength(50).IsRequired();
            entity.HasIndex(item => item.CreatedAtUtc);
            entity.HasIndex(item => item.DataSourceId);
        });

        builder.Entity<MaintenanceSettings>(entity =>
        {
            entity.ToTable("GatewayMaintenanceSettings");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.CleanupTimeLocal).HasMaxLength(5).IsRequired();
            entity.Property(item => item.ApprovalExpirationMinutes).HasDefaultValue(15).IsRequired();
            entity.Property(item => item.LastCleanupSummary).HasMaxLength(500);
        });

        builder.Entity<ProjectDefinition>(entity =>
        {
            entity.ToTable("GatewayProjects");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.Code).IsUnique();
            entity.Property(item => item.Code).HasMaxLength(100).IsRequired();
            entity.Property(item => item.Name).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Description).HasMaxLength(2_000).IsRequired();
        });

        builder.Entity<ProjectDataSourceLink>(entity =>
        {
            entity.ToTable("GatewayProjectDataSources");
            entity.HasKey(item => new { item.ProjectId, item.DataSourceId });
            entity.HasOne<ProjectDefinition>()
                .WithMany()
                .HasForeignKey(item => item.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<DataSourceDefinition>()
                .WithMany()
                .HasForeignKey(item => item.DataSourceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<LogSourceDefinition>(entity =>
        {
            entity.ToTable("GatewayLogSources");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.Key).IsUnique();
            entity.Property(item => item.Key).HasMaxLength(100).IsRequired();
            entity.Property(item => item.Name).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Type).HasConversion<string>().HasMaxLength(50);
            entity.Property(item => item.Endpoint).HasMaxLength(2_000).IsRequired();
            entity.Property(item => item.NLogTargetName).HasMaxLength(200).IsRequired();
            entity.Property(item => item.NLogLayout).HasMaxLength(4_000).IsRequired();
            entity.Property(item => item.ProtectedConfiguration).IsRequired();
            entity.Property(item => item.ProtectedApiKey).IsRequired();
        });

        builder.Entity<ProjectLogSourceLink>(entity =>
        {
            entity.ToTable("GatewayProjectLogSources");
            entity.HasKey(item => new { item.ProjectId, item.LogSourceId });
            entity.HasOne<ProjectDefinition>()
                .WithMany()
                .HasForeignKey(item => item.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<LogSourceDefinition>()
                .WithMany()
                .HasForeignKey(item => item.LogSourceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MonitorTargetDefinition>(entity =>
        {
            entity.ToTable("GatewayMonitorTargets");
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.Key).IsUnique();
            entity.Property(item => item.Key).HasMaxLength(100).IsRequired();
            entity.Property(item => item.Name).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Type).HasConversion<string>().HasMaxLength(30);
            entity.Property(item => item.IngestSecretHash).HasMaxLength(200).IsRequired();
            entity.Property(item => item.HostName).HasMaxLength(200).IsRequired();
            entity.Property(item => item.OsDescription).HasMaxLength(500).IsRequired();
            entity.Property(item => item.MetricSelection).HasMaxLength(4000).IsRequired();
        });

        builder.Entity<ServerMetricSample>(entity =>
        {
            entity.ToTable("GatewayServerMetricSamples");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedOnAdd();
            entity.Property(item => item.ExtendedMetricsJson).IsRequired();
            entity.HasIndex(item => new { item.MonitorTargetId, item.Id });
            entity.HasOne<MonitorTargetDefinition>()
                .WithMany()
                .HasForeignKey(item => item.MonitorTargetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ProjectMonitorTargetLink>(entity =>
        {
            entity.ToTable("GatewayProjectMonitorTargets");
            entity.HasKey(item => new { item.ProjectId, item.MonitorTargetId });
            entity.HasOne<ProjectDefinition>().WithMany().HasForeignKey(item => item.ProjectId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<MonitorTargetDefinition>().WithMany().HasForeignKey(item => item.MonitorTargetId).OnDelete(DeleteBehavior.Cascade);
        });

    }
}
