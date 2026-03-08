using AgentHub.Orchestration.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgentHub.Orchestration.Data;

public class AgentHubDbContext(DbContextOptions<AgentHubDbContext> options)
    : DbContext(options)
{
    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();
    public DbSet<SessionEventEntity> Events => Set<SessionEventEntity>();
    public DbSet<HostEntity> Hosts => Set<HostEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SessionEntity>(e =>
        {
            e.HasKey(x => x.SessionId);
            e.HasIndex(x => x.OwnerUserId);
            e.HasIndex(x => x.State);
            e.Property(x => x.State).HasConversion<string>();
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
    }
}
