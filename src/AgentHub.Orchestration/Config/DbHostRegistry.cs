using AgentHub.Contracts;
using AgentHub.Orchestration.Data;
using Microsoft.EntityFrameworkCore;

namespace AgentHub.Orchestration.Config;

public sealed class DbHostRegistry(IDbContextFactory<AgentHubDbContext> dbFactory) : IHostRegistry
{
    public async Task<IReadOnlyList<HostRecord>> ListAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entities = await db.Hosts
            .Where(h => h.Enabled)
            .ToListAsync(ct);
        return entities.Select(e => e.ToDto()).ToList();
    }

    public async Task<HostRecord?> GetAsync(string hostId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entity = await db.Hosts.FindAsync([hostId], ct);
        return entity?.ToDto();
    }
}
