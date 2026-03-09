using AgentHub.Contracts;
using AgentHub.Orchestration.Monitoring;
using Xunit;

namespace AgentHub.Tests;

public class HostMetricCacheTests
{
    [Fact]
    public void Update_Then_Get_ReturnsStoredSnapshot()
    {
        var cache = new HostMetricCache();
        var snapshot = new HostSnapshot(
            "host-1", 45.0, 2048, 4096, null,
            DateTimeOffset.UtcNow, null);

        cache.Update("host-1", snapshot);
        var result = cache.Get("host-1");

        Assert.NotNull(result);
        Assert.Equal("host-1", result.HostId);
        Assert.Equal(45.0, result.CpuPercent);
        Assert.Equal(2048, result.MemUsedMb);
        Assert.Equal(4096, result.MemTotalMb);
    }

    [Fact]
    public void Get_UnknownHostId_ReturnsNull()
    {
        var cache = new HostMetricCache();

        var result = cache.Get("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public void GetAll_ReturnsAllStoredSnapshots()
    {
        var cache = new HostMetricCache();
        var snap1 = new HostSnapshot("host-1", 10.0, 1024, 2048, null, DateTimeOffset.UtcNow, null);
        var snap2 = new HostSnapshot("host-2", 20.0, 2048, 4096, null, DateTimeOffset.UtcNow, null);

        cache.Update("host-1", snap1);
        cache.Update("host-2", snap2);

        var all = cache.GetAll();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, s => s.HostId == "host-1");
        Assert.Contains(all, s => s.HostId == "host-2");
    }

    [Fact]
    public void Update_OverwritesPreviousSnapshot()
    {
        var cache = new HostMetricCache();
        var original = new HostSnapshot("host-1", 10.0, 1024, 2048, null, DateTimeOffset.UtcNow, null);
        var updated = new HostSnapshot("host-1", 90.0, 3072, 4096, null, DateTimeOffset.UtcNow, null);

        cache.Update("host-1", original);
        cache.Update("host-1", updated);

        var result = cache.Get("host-1");
        Assert.NotNull(result);
        Assert.Equal(90.0, result.CpuPercent);
        Assert.Equal(3072, result.MemUsedMb);
    }

    [Fact]
    public void ConcurrentUpdates_DoNotThrowOrCorrupt()
    {
        var cache = new HostMetricCache();
        var tasks = new List<Task>();

        for (int i = 0; i < 100; i++)
        {
            var hostId = $"host-{i % 10}";
            var cpu = (double)i;
            tasks.Add(Task.Run(() =>
            {
                var snap = new HostSnapshot(hostId, cpu, 1024, 2048, null, DateTimeOffset.UtcNow, null);
                cache.Update(hostId, snap);
            }));
        }

        Task.WaitAll(tasks.ToArray());

        var all = cache.GetAll();
        Assert.Equal(10, all.Count);
    }

    [Fact]
    public void UpdateMetrics_PreservesExistingInventory()
    {
        var cache = new HostMetricCache();
        var inventory = new HostInventory(
            [new AgentInfo("claude", "1.0.0", "/usr/bin/claude", ["code-generation"])],
            100.0,
            "2.40.0");
        var initial = new HostSnapshot("host-1", 50.0, 2048, 4096, inventory, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        cache.Update("host-1", initial);
        cache.UpdateMetrics("host-1", 75.0, 3072, 4096);

        var result = cache.Get("host-1");
        Assert.NotNull(result);
        Assert.Equal(75.0, result.CpuPercent);
        Assert.Equal(3072, result.MemUsedMb);
        Assert.NotNull(result.Inventory);
        Assert.Equal("claude", result.Inventory.Agents[0].Name);
    }

    [Fact]
    public void UpdateInventory_PreservesExistingMetrics()
    {
        var cache = new HostMetricCache();
        var initial = new HostSnapshot("host-1", 50.0, 2048, 4096, null, DateTimeOffset.UtcNow, null);

        cache.Update("host-1", initial);

        var inventory = new HostInventory(
            [new AgentInfo("codex", "0.1.0", "/usr/bin/codex", ["code-generation"])],
            200.0,
            "2.41.0");
        cache.UpdateInventory("host-1", inventory);

        var result = cache.Get("host-1");
        Assert.NotNull(result);
        Assert.Equal(50.0, result.CpuPercent);
        Assert.Equal(2048, result.MemUsedMb);
        Assert.NotNull(result.Inventory);
        Assert.Equal("codex", result.Inventory.Agents[0].Name);
    }
}
