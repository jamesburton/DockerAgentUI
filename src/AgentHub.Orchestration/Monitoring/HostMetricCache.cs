using System.Collections.Concurrent;
using AgentHub.Contracts;

namespace AgentHub.Orchestration.Monitoring;

public sealed record HostSnapshot(
    string HostId,
    double? CpuPercent,
    long? MemUsedMb,
    long? MemTotalMb,
    HostInventory? Inventory,
    DateTimeOffset? MetricsUpdatedUtc,
    DateTimeOffset? InventoryUpdatedUtc);

public sealed class HostMetricCache
{
    private readonly ConcurrentDictionary<string, HostSnapshot> _cache = new();

    public void Update(string hostId, HostSnapshot snapshot)
    {
        _cache[hostId] = snapshot;
    }

    public void UpdateMetrics(string hostId, double? cpu, long? memUsed, long? memTotal)
    {
        _cache.AddOrUpdate(
            hostId,
            _ => new HostSnapshot(hostId, cpu, memUsed, memTotal, null, DateTimeOffset.UtcNow, null),
            (_, existing) => new HostSnapshot(
                existing.HostId, cpu, memUsed, memTotal,
                existing.Inventory,
                DateTimeOffset.UtcNow,
                existing.InventoryUpdatedUtc));
    }

    public void UpdateInventory(string hostId, HostInventory inventory)
    {
        _cache.AddOrUpdate(
            hostId,
            _ => new HostSnapshot(hostId, null, null, null, inventory, null, DateTimeOffset.UtcNow),
            (_, existing) => new HostSnapshot(
                existing.HostId,
                existing.CpuPercent,
                existing.MemUsedMb,
                existing.MemTotalMb,
                inventory,
                existing.MetricsUpdatedUtc,
                DateTimeOffset.UtcNow));
    }

    public HostSnapshot? Get(string hostId)
    {
        return _cache.TryGetValue(hostId, out var snapshot) ? snapshot : null;
    }

    public IReadOnlyList<HostSnapshot> GetAll()
    {
        return _cache.Values.ToList().AsReadOnly();
    }
}
