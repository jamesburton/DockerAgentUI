using System.Text.Json;
using AgentHub.Contracts;
using AgentHub.Orchestration.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentHub.Orchestration.Data;

public sealed class HostSeedingService(
    IDbContextFactory<AgentHubDbContext> dbFactory,
    string hostsJsonPath,
    ILogger<HostSeedingService> logger) : IHostedService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(hostsJsonPath))
        {
            logger.LogWarning("hosts.json not found at {Path}, skipping host seeding", hostsJsonPath);
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(hostsJsonPath, cancellationToken);
            var hosts = JsonSerializer.Deserialize<List<HostRecord>>(json, JsonOpts);

            if (hosts is null || hosts.Count == 0)
            {
                logger.LogWarning("hosts.json is empty or invalid, skipping host seeding");
                return;
            }

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            foreach (var host in hosts)
            {
                var existing = await db.Hosts.FindAsync([host.HostId], cancellationToken);
                if (existing is null)
                {
                    // Insert new host
                    var entity = host.ToEntity();
                    db.Hosts.Add(entity);
                    logger.LogInformation("Seeding new host: {HostId} ({DisplayName})", host.HostId, host.DisplayName);
                }
                else
                {
                    // Upsert: file values win for config fields, DB wins for runtime state
                    existing.DisplayName = host.DisplayName;
                    existing.Backend = host.Backend;
                    existing.Os = host.Os;
                    existing.Enabled = host.Enabled;
                    existing.AllowSsh = host.AllowSsh;
                    existing.LabelsJson = host.Labels is not null
                        ? JsonSerializer.Serialize(host.Labels, JsonOpts)
                        : null;
                    existing.Address = host.Address;
                    // Keep existing LastSeenUtc and Status (runtime state)
                    logger.LogInformation("Updating host config: {HostId} ({DisplayName})", host.HostId, host.DisplayName);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Host seeding complete: {Count} hosts processed", hosts.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error seeding hosts from {Path}", hostsJsonPath);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
