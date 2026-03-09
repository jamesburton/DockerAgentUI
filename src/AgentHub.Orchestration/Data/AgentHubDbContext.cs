using AgentHub.Orchestration.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgentHub.Orchestration.Data;

public class AgentHubDbContext(DbContextOptions<AgentHubDbContext> options)
    : DbContext(options)
{
    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();
    public DbSet<SessionEventEntity> Events => Set<SessionEventEntity>();
    public DbSet<HostEntity> Hosts => Set<HostEntity>();
    public DbSet<ApprovalEntity> Approvals => Set<ApprovalEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SessionEntity>(e =>
        {
            e.HasKey(x => x.SessionId);
            e.HasIndex(x => x.OwnerUserId);
            e.HasIndex(x => x.State);
            e.Property(x => x.State).HasConversion<string>();

            // Self-referencing FK for multi-agent coordination
            e.HasOne<SessionEntity>()
                .WithMany()
                .HasForeignKey(x => x.ParentSessionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SessionEventEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.HasIndex(x => new { x.SessionId, x.Id });
            e.Property(x => x.Kind).HasConversion<string>();

            e.HasOne(x => x.Session)
                .WithMany(s => s.Events)
                .HasForeignKey(x => x.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HostEntity>(e =>
        {
            e.HasKey(x => x.HostId);
        });

        modelBuilder.Entity<ApprovalEntity>(e =>
        {
            e.HasKey(x => x.ApprovalId);
            e.HasIndex(x => x.SessionId);
            e.HasIndex(x => x.Status);
            e.Property(x => x.Status).HasConversion<string>();

            e.HasOne(x => x.Session)
                .WithMany(s => s.Approvals)
                .HasForeignKey(x => x.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
