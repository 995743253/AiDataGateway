using AiDataGateway.Domain.Approvals;
using AiDataGateway.Domain.Auditing;
using AiDataGateway.Domain.DataSources;
using AiDataGateway.Domain.Maintenance;
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
            entity.Property(item => item.LastCleanupSummary).HasMaxLength(500);
        });
    }
}
