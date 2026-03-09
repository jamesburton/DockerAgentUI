using AgentHub.Contracts;
using Xunit;

namespace AgentHub.Tests;

/// <summary>
/// Tests the pure logic patterns used in FleetOverview.OnHostMetricsReceived:
/// - Dictionary meta parsing with TryGetValue + TryParse chains
/// - HostRecord 'with' expression updates for CPU/memory metrics
/// These mirror the private OnHostMetricsReceived method without requiring Blazor rendering.
/// </summary>
public class FleetOverviewTests
{
    // Replicates the exact parsing + patching logic from FleetOverview.OnHostMetricsReceived
    private static bool TryApplyHostMetrics(List<HostRecord> hosts, Dictionary<string, string> meta)
    {
        if (!meta.TryGetValue("hostId", out var hostId)) return false;
        var idx = hosts.FindIndex(h => h.HostId == hostId);
        if (idx < 0) return false;

        double? cpu = meta.TryGetValue("cpu", out var cpuStr) && double.TryParse(cpuStr, out var cpuVal) ? cpuVal : null;
        long? memUsed = meta.TryGetValue("memUsedMb", out var muStr) && long.TryParse(muStr, out var muVal) ? muVal : null;
        long? memTotal = meta.TryGetValue("memTotalMb", out var mtStr) && long.TryParse(mtStr, out var mtVal) ? mtVal : null;

        hosts[idx] = hosts[idx] with { CpuPercent = cpu, MemUsedMb = memUsed, MemTotalMb = memTotal };
        return true;
    }

    private static List<HostRecord> MakeHosts(params string[] hostIds)
    {
        return hostIds.Select(id => new HostRecord(
            HostId: id,
            DisplayName: $"Host {id}",
            Backend: "ssh",
            Os: "linux",
            Enabled: true,
            AllowSsh: true
        )).ToList();
    }

    [Fact]
    public void OnHostMetricsReceived_UpdatesHostCpuMemFromMeta()
    {
        var hosts = MakeHosts("h1", "h2");
        var meta = new Dictionary<string, string>
        {
            ["hostId"] = "h1",
            ["cpu"] = "42.5",
            ["memUsedMb"] = "2048",
            ["memTotalMb"] = "8192"
        };

        var applied = TryApplyHostMetrics(hosts, meta);

        Assert.True(applied);
        Assert.Equal(42.5, hosts[0].CpuPercent);
        Assert.Equal(2048L, hosts[0].MemUsedMb);
        Assert.Equal(8192L, hosts[0].MemTotalMb);
        // h2 unchanged
        Assert.Null(hosts[1].CpuPercent);
    }

    [Fact]
    public void OnHostMetricsReceived_IgnoresEventsWithoutHostId()
    {
        var hosts = MakeHosts("h1");
        var meta = new Dictionary<string, string>
        {
            ["cpu"] = "50.0",
            ["memUsedMb"] = "1024",
            ["memTotalMb"] = "4096"
        };

        var applied = TryApplyHostMetrics(hosts, meta);

        Assert.False(applied);
        Assert.Null(hosts[0].CpuPercent);
    }

    [Fact]
    public void OnHostMetricsReceived_IgnoresEventsForUnknownHostId()
    {
        var hosts = MakeHosts("h1");
        var meta = new Dictionary<string, string>
        {
            ["hostId"] = "unknown-host",
            ["cpu"] = "50.0",
            ["memUsedMb"] = "1024",
            ["memTotalMb"] = "4096"
        };

        var applied = TryApplyHostMetrics(hosts, meta);

        Assert.False(applied);
        Assert.Null(hosts[0].CpuPercent);
    }

    [Fact]
    public void OnHostMetricsReceived_HandlesPartialMetricData_CpuOnly()
    {
        var hosts = MakeHosts("h1");
        var meta = new Dictionary<string, string>
        {
            ["hostId"] = "h1",
            ["cpu"] = "75.3"
        };

        var applied = TryApplyHostMetrics(hosts, meta);

        Assert.True(applied);
        Assert.Equal(75.3, hosts[0].CpuPercent);
        Assert.Null(hosts[0].MemUsedMb);
        Assert.Null(hosts[0].MemTotalMb);
    }

    [Fact]
    public void OnHostMetricsReceived_HandlesPartialMetricData_MemoryOnly()
    {
        var hosts = MakeHosts("h1");
        var meta = new Dictionary<string, string>
        {
            ["hostId"] = "h1",
            ["memUsedMb"] = "4096",
            ["memTotalMb"] = "16384"
        };

        var applied = TryApplyHostMetrics(hosts, meta);

        Assert.True(applied);
        Assert.Null(hosts[0].CpuPercent);
        Assert.Equal(4096L, hosts[0].MemUsedMb);
        Assert.Equal(16384L, hosts[0].MemTotalMb);
    }

    [Fact]
    public void OnHostMetricsReceived_HandlesInvalidNumericValues_GracefullyNulls()
    {
        var hosts = MakeHosts("h1");
        var meta = new Dictionary<string, string>
        {
            ["hostId"] = "h1",
            ["cpu"] = "not-a-number",
            ["memUsedMb"] = "also-bad",
            ["memTotalMb"] = "8192"
        };

        var applied = TryApplyHostMetrics(hosts, meta);

        Assert.True(applied);
        Assert.Null(hosts[0].CpuPercent);
        Assert.Null(hosts[0].MemUsedMb);
        Assert.Equal(8192L, hosts[0].MemTotalMb);
    }

    [Fact]
    public void HostRecord_WithExpression_PreservesNonMetricFields()
    {
        var original = new HostRecord(
            HostId: "h1",
            DisplayName: "My Host",
            Backend: "ssh",
            Os: "linux",
            Enabled: true,
            AllowSsh: true,
            Labels: new Dictionary<string, string> { ["env"] = "prod" },
            Address: "192.168.1.1"
        );

        var updated = original with { CpuPercent = 55.0, MemUsedMb = 2048, MemTotalMb = 8192 };

        Assert.Equal("h1", updated.HostId);
        Assert.Equal("My Host", updated.DisplayName);
        Assert.Equal("ssh", updated.Backend);
        Assert.Equal("linux", updated.Os);
        Assert.True(updated.Enabled);
        Assert.True(updated.AllowSsh);
        Assert.Equal("prod", updated.Labels!["env"]);
        Assert.Equal("192.168.1.1", updated.Address);
        Assert.Equal(55.0, updated.CpuPercent);
        Assert.Equal(2048L, updated.MemUsedMb);
        Assert.Equal(8192L, updated.MemTotalMb);
    }
}
