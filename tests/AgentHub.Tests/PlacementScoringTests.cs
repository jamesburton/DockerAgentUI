using AgentHub.Contracts;
using AgentHub.Orchestration;
using AgentHub.Orchestration.Coordinator;
using AgentHub.Orchestration.Monitoring;
using AgentHub.Orchestration.Placement;
using Microsoft.Extensions.Options;
using Xunit;

namespace AgentHub.Tests;

[Trait("Category", "Phase10")]
public class PlacementScoringTests
{
    private static IOptions<PlacementOptions> DefaultOptions() =>
        Options.Create(new PlacementOptions());

    private static NodeCapability MakeNode(string id, string backend = "ssh", int cpu = 8, int mem = 16384) =>
        new(id, backend, "linux", cpu, mem, false, true, new Dictionary<string, string>());

    private static void SeedMetrics(HostMetricCache cache, string hostId, double cpu, long memUsed, long memTotal, double? diskFreeGb = 50.0, DateTimeOffset? updatedUtc = null)
    {
        var ts = updatedUtc ?? DateTimeOffset.UtcNow;
        var inventory = diskFreeGb.HasValue ? new HostInventory([], diskFreeGb, null) : null;
        cache.UpdateMetrics(hostId, cpu, memUsed, memTotal);
        if (inventory != null)
            cache.UpdateInventory(hostId, inventory);
        // Force timestamp if provided: re-update metrics so MetricsUpdatedUtc is set
        // (HostMetricCache.UpdateMetrics always uses DateTimeOffset.UtcNow internally)
    }

    [Fact]
    public void HigherScoredHost_IsSelected()
    {
        var cache = new HostMetricCache();
        var tracker = new ActiveSessionTracker();
        var engine = new SimplePlacementEngine(DefaultOptions(), cache, tracker);

        // Host1: high CPU usage (bad), Host2: low CPU usage (good)
        SeedMetrics(cache, "host1", cpu: 80, memUsed: 8000, memTotal: 16384);
        SeedMetrics(cache, "host2", cpu: 20, memUsed: 4000, memTotal: 16384);

        var nodes = new List<NodeCapability>
        {
            MakeNode("host1"),
            MakeNode("host2")
        };

        var req = new SessionRequirements(ExecutionMode: ExecutionMode.Ssh, AcceptRisk: true);
        var result = engine.ChooseNode("user1", req, nodes);

        Assert.Equal("host2", result.NodeId);
    }

    [Fact]
    public void HostWithNoMetrics_IsExcluded()
    {
        var cache = new HostMetricCache();
        var tracker = new ActiveSessionTracker();
        var engine = new SimplePlacementEngine(DefaultOptions(), cache, tracker);

        // Only host2 has metrics
        SeedMetrics(cache, "host2", cpu: 50, memUsed: 8000, memTotal: 16384);

        var nodes = new List<NodeCapability>
        {
            MakeNode("host1"),
            MakeNode("host2")
        };

        var req = new SessionRequirements(ExecutionMode: ExecutionMode.Ssh, AcceptRisk: true);
        var result = engine.ChooseNode("user1", req, nodes);

        Assert.Equal("host2", result.NodeId);
    }

    [Fact]
    public void HostWithLowDisk_IsExcluded()
    {
        var cache = new HostMetricCache();
        var tracker = new ActiveSessionTracker();
        var engine = new SimplePlacementEngine(DefaultOptions(), cache, tracker);

        // host1 has low disk
        SeedMetrics(cache, "host1", cpu: 20, memUsed: 4000, memTotal: 16384, diskFreeGb: 2.0);
        SeedMetrics(cache, "host2", cpu: 50, memUsed: 8000, memTotal: 16384, diskFreeGb: 50.0);

        var nodes = new List<NodeCapability>
        {
            MakeNode("host1"),
            MakeNode("host2")
        };

        var req = new SessionRequirements(ExecutionMode: ExecutionMode.Ssh, AcceptRisk: true);
        var result = engine.ChooseNode("user1", req, nodes);

        Assert.Equal("host2", result.NodeId);
    }

    [Fact]
    public void HostAtMaxSessions_IsExcluded()
    {
        var cache = new HostMetricCache();
        var tracker = new ActiveSessionTracker();
        var opts = new PlacementOptions { MaxSessionsPerHost = 3 };
        var engine = new SimplePlacementEngine(Options.Create(opts), cache, tracker);

        SeedMetrics(cache, "host1", cpu: 10, memUsed: 2000, memTotal: 16384);
        SeedMetrics(cache, "host2", cpu: 50, memUsed: 8000, memTotal: 16384);

        // host1 at max sessions
        tracker.Increment("host1");
        tracker.Increment("host1");
        tracker.Increment("host1");

        var nodes = new List<NodeCapability>
        {
            MakeNode("host1"),
            MakeNode("host2")
        };

        var req = new SessionRequirements(ExecutionMode: ExecutionMode.Ssh, AcceptRisk: true);
        var result = engine.ChooseNode("user1", req, nodes);

        Assert.Equal("host2", result.NodeId);
    }

    [Fact]
    public void TargetHostId_BypassesScoring()
    {
        var cache = new HostMetricCache();
        var tracker = new ActiveSessionTracker();
        var engine = new SimplePlacementEngine(DefaultOptions(), cache, tracker);

        // host1 has terrible metrics but is explicitly targeted
        SeedMetrics(cache, "host1", cpu: 99, memUsed: 15000, memTotal: 16384);
        SeedMetrics(cache, "host2", cpu: 10, memUsed: 2000, memTotal: 16384);

        var nodes = new List<NodeCapability>
        {
            MakeNode("host1"),
            MakeNode("host2")
        };

        var req = new SessionRequirements(ExecutionMode: ExecutionMode.Ssh, AcceptRisk: true, TargetHostId: "host1");
        var result = engine.ChooseNode("user1", req, nodes);

        Assert.Equal("host1", result.NodeId);
    }

    [Fact]
    public void AllHostsExcluded_ThrowsInvalidOperationException()
    {
        var cache = new HostMetricCache();
        var tracker = new ActiveSessionTracker();
        var engine = new SimplePlacementEngine(DefaultOptions(), cache, tracker);

        // No metrics for any host
        var nodes = new List<NodeCapability> { MakeNode("host1") };

        var req = new SessionRequirements(ExecutionMode: ExecutionMode.Ssh, AcceptRisk: true);

        var ex = Assert.Throws<InvalidOperationException>(() => engine.ChooseNode("user1", req, nodes));
        Assert.Contains("No eligible node", ex.Message);
    }

    [Fact]
    public void MoreSessionsLowerScore()
    {
        var cache = new HostMetricCache();
        var tracker = new ActiveSessionTracker();
        var engine = new SimplePlacementEngine(DefaultOptions(), cache, tracker);

        // Identical metrics, but host1 has more sessions
        SeedMetrics(cache, "host1", cpu: 30, memUsed: 4000, memTotal: 16384);
        SeedMetrics(cache, "host2", cpu: 30, memUsed: 4000, memTotal: 16384);

        tracker.Increment("host1");
        tracker.Increment("host1");
        tracker.Increment("host1");

        var nodes = new List<NodeCapability>
        {
            MakeNode("host1"),
            MakeNode("host2")
        };

        var req = new SessionRequirements(ExecutionMode: ExecutionMode.Ssh, AcceptRisk: true);
        var result = engine.ChooseNode("user1", req, nodes);

        Assert.Equal("host2", result.NodeId);
    }
}
