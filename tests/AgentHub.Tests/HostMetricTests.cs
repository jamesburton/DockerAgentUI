using AgentHub.Contracts;
using AgentHub.Orchestration.Data;
using AgentHub.Orchestration.Data.Entities;
using AgentHub.Orchestration.Monitoring;
using Xunit;

namespace AgentHub.Tests;

public class HostMetricTests
{
    [Fact]
    public void ToDto_PopulatesMetricFields_WhenEntityHasMetrics()
    {
        var entity = new HostEntity
        {
            HostId = "h1",
            DisplayName = "Test Host",
            Backend = "ssh",
            Os = "linux",
            Enabled = true,
            AllowSsh = true,
            CpuPercent = 42.5,
            MemUsedMb = 2048,
            MemTotalMb = 8192
        };

        var dto = entity.ToDto();

        Assert.Equal(42.5, dto.CpuPercent);
        Assert.Equal(2048, dto.MemUsedMb);
        Assert.Equal(8192, dto.MemTotalMb);
    }

    [Fact]
    public void ToDto_ReturnsNullMetrics_WhenEntityHasNoMetrics()
    {
        var entity = new HostEntity
        {
            HostId = "h2",
            DisplayName = "Empty Host",
            Backend = "ssh",
            Os = "windows",
            Enabled = true,
            AllowSsh = true
        };

        var dto = entity.ToDto();

        Assert.Null(dto.CpuPercent);
        Assert.Null(dto.MemUsedMb);
        Assert.Null(dto.MemTotalMb);
    }

    [Theory]
    [InlineData("45.2|16384|8192", 45.2, 16384, 8192)]
    [InlineData("0.0|1024|512", 0.0, 1024, 512)]
    [InlineData("99.9|32768|30000", 99.9, 32768, 30000)]
    public void ParseMetricOutput_ParsesValidOutput(string output, double expectedCpu, long expectedTotal, long expectedUsed)
    {
        var result = HostMetricPollingService.ParseMetricOutput(output);

        Assert.NotNull(result);
        var (cpu, memTotal, memUsed) = result.Value;
        Assert.Equal(expectedCpu, cpu, precision: 1);
        Assert.Equal(expectedTotal, memTotal);
        Assert.Equal(expectedUsed, memUsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("45.2|abc|def")]
    public void ParseMetricOutput_ReturnsNull_ForInvalidOutput(string output)
    {
        var result = HostMetricPollingService.ParseMetricOutput(output);

        Assert.Null(result);
    }

    [Fact]
    public void GetMetricCommand_ReturnsWindowsCommand_ForWindowsOs()
    {
        var cmd = HostMetricPollingService.GetMetricCommand("Windows");
        Assert.Contains("powershell", cmd, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetMetricCommand_ReturnsLinuxCommand_ForLinuxOs()
    {
        var cmd = HostMetricPollingService.GetMetricCommand("linux");
        Assert.Contains("/proc/stat", cmd);
    }

    [Fact]
    public void GetMetricCommand_ReturnsMacCommand_ForMacOs()
    {
        var cmd = HostMetricPollingService.GetMetricCommand("macos");
        Assert.Contains("sysctl", cmd);
    }

    [Fact]
    public void GetMetricCommand_ReturnsMacCommand_ForDarwinOs()
    {
        var cmd = HostMetricPollingService.GetMetricCommand("darwin");
        Assert.Contains("sysctl", cmd);
    }

    [Fact]
    public void SessionEventKind_ContainsHostMetrics()
    {
        Assert.True(Enum.IsDefined(typeof(SessionEventKind), SessionEventKind.HostMetrics));
    }
}
