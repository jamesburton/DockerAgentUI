using AgentHub.Contracts;
using AgentHub.Orchestration.Monitoring;
using Xunit;

namespace AgentHub.Tests;

public class HostInventoryTests
{
    // --- GetInventoryCommand tests ---

    [Fact]
    public void GetInventoryCommand_Windows_ReturnsPowerShellCommand()
    {
        var agents = CreateSampleAgentConfigs();
        var cmd = HostInventoryPollingService.GetInventoryCommand("windows", agents);

        Assert.NotNull(cmd);
        Assert.Contains("powershell", cmd, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ConvertTo-Json", cmd, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetInventoryCommand_Linux_ReturnsBashCommand()
    {
        var agents = CreateSampleAgentConfigs();
        var cmd = HostInventoryPollingService.GetInventoryCommand("linux", agents);

        Assert.NotNull(cmd);
        Assert.Contains("bash", cmd, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("df -BG", cmd);
        Assert.DoesNotContain("jq", cmd);
    }

    [Fact]
    public void GetInventoryCommand_MacOs_ReturnsBashCommand()
    {
        var agents = CreateSampleAgentConfigs();
        var cmd = HostInventoryPollingService.GetInventoryCommand("macos", agents);

        Assert.NotNull(cmd);
        Assert.Contains("bash", cmd, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("df -g", cmd);
        Assert.DoesNotContain("jq", cmd);
    }

    [Fact]
    public void GetInventoryCommand_Unsupported_ReturnsNull()
    {
        var agents = CreateSampleAgentConfigs();
        var cmd = HostInventoryPollingService.GetInventoryCommand("unsupported", agents);

        Assert.Null(cmd);
    }

    // --- ParseInventoryOutput tests ---

    [Fact]
    public void ParseInventoryOutput_ValidJson_TwoAgents_ReturnsCorrectInventory()
    {
        var json = """
        {
            "agents": [
                {"name":"claude","version":"claude-code/1.2.3 linux","path":"/usr/bin/claude"},
                {"name":"codex","version":"0.5.0","path":"/usr/bin/codex"}
            ],
            "diskFreeGb": 150.5,
            "gitVersion": "git version 2.40.0"
        }
        """;

        var result = HostInventoryPollingService.ParseInventoryOutput(json);

        Assert.NotNull(result);
        Assert.Equal(2, result.Agents.Count);
        Assert.Equal("claude", result.Agents[0].Name);
        Assert.Equal("claude-code/1.2.3 linux", result.Agents[0].Version);
        Assert.Equal("/usr/bin/claude", result.Agents[0].Path);
        Assert.Equal("codex", result.Agents[1].Name);
        Assert.Equal(150.5, result.DiskFreeGb);
        Assert.Equal("git version 2.40.0", result.GitVersion);
    }

    [Fact]
    public void ParseInventoryOutput_PartialJson_OneMissingAgent_ReturnsPartialInventory()
    {
        var json = """
        {
            "agents": [
                {"name":"claude","version":"1.0.0","path":"/usr/bin/claude"}
            ],
            "diskFreeGb": 50.0,
            "gitVersion": null
        }
        """;

        var result = HostInventoryPollingService.ParseInventoryOutput(json);

        Assert.NotNull(result);
        Assert.Single(result.Agents);
        Assert.Equal("claude", result.Agents[0].Name);
        Assert.Equal(50.0, result.DiskFreeGb);
        Assert.Null(result.GitVersion);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{malformed")]
    public void ParseInventoryOutput_InvalidInput_ReturnsNull(string? input)
    {
        var result = HostInventoryPollingService.ParseInventoryOutput(input);

        Assert.Null(result);
    }

    // --- ExtractVersion tests ---

    [Fact]
    public void ExtractVersion_ValidOutput_ReturnsVersion()
    {
        var version = HostInventoryPollingService.ExtractVersion(
            "claude-code/1.2.3 linux x64",
            @"claude-code/(\d+\.\d+\.\d+)");

        Assert.Equal("1.2.3", version);
    }

    [Fact]
    public void ExtractVersion_NullInput_ReturnsNull()
    {
        var version = HostInventoryPollingService.ExtractVersion(
            null,
            @"(\d+\.\d+\.\d+)");

        Assert.Null(version);
    }

    [Fact]
    public void ExtractVersion_NoMatch_ReturnsNull()
    {
        var version = HostInventoryPollingService.ExtractVersion(
            "no version here",
            @"claude-code/(\d+\.\d+\.\d+)");

        Assert.Null(version);
    }

    // --- ResolveCapabilities tests ---

    [Fact]
    public void ResolveCapabilities_VersionMeetsMinimum_ReturnsCapabilities()
    {
        var capabilityMap = new Dictionary<string, List<string>>
        {
            ["1.0.0+"] = ["code-generation", "file-edit"]
        };

        var capabilities = HostInventoryPollingService.ResolveCapabilities("1.2.3", capabilityMap);

        Assert.Equal(2, capabilities.Count);
        Assert.Contains("code-generation", capabilities);
        Assert.Contains("file-edit", capabilities);
    }

    [Fact]
    public void ResolveCapabilities_VersionBelowMinimum_ReturnsEmptyList()
    {
        var capabilityMap = new Dictionary<string, List<string>>
        {
            ["1.0.0+"] = ["code-generation"]
        };

        var capabilities = HostInventoryPollingService.ResolveCapabilities("0.5.0", capabilityMap);

        Assert.Empty(capabilities);
    }

    [Fact]
    public void ResolveCapabilities_NullVersion_ReturnsEmptyList()
    {
        var capabilityMap = new Dictionary<string, List<string>>
        {
            ["1.0.0+"] = ["code-generation"]
        };

        var capabilities = HostInventoryPollingService.ResolveCapabilities(null, capabilityMap);

        Assert.Empty(capabilities);
    }

    // --- Helpers ---

    private static List<AgentConfig> CreateSampleAgentConfigs()
    {
        return
        [
            new AgentConfig("claude", "--version", @"claude-code/(\d+\.\d+\.\d+)",
                new Dictionary<string, List<string>> { ["1.0.0+"] = ["code-generation", "file-edit", "bash"] }),
            new AgentConfig("codex", "--version", @"(\d+\.\d+\.\d+)",
                new Dictionary<string, List<string>> { ["0.1.0+"] = ["code-generation"] })
        ];
    }
}
