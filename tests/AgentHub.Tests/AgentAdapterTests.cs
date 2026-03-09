using System.Text.Json;
using AgentHub.Contracts;
using AgentHub.Orchestration.Agents;
using Xunit;

namespace AgentHub.Tests;

public class AgentAdapterTests
{
    private readonly ClaudeCodeAdapter _adapter = new();

    [Fact]
    public void ClaudeCodeAdapter_AgentType_Returns_ClaudeCode()
    {
        Assert.Equal("claude-code", _adapter.AgentType);
    }

    [Fact]
    public void BuildCommandArgs_Includes_Prompt_And_OutputFormat()
    {
        var request = new AgentStartRequest(
            SessionId: "sess-1",
            WorkingDirectory: "/tmp",
            Prompt: "Hello world");

        var args = _adapter.BuildCommandArgs(request);

        Assert.Contains("-p", args);
        Assert.Contains("Hello world", args);
        Assert.Contains("--output-format", args);
        Assert.Contains("stream-json", args);
    }

    [Fact]
    public void BuildCommandArgs_Includes_DangerouslySkipPermissions_When_True()
    {
        var request = new AgentStartRequest(
            SessionId: "sess-1",
            WorkingDirectory: "/tmp",
            Prompt: "test",
            Permissions: new AgentPermissions(SkipPermissionPrompts: true));

        var args = _adapter.BuildCommandArgs(request);

        Assert.Contains("--dangerously-skip-permissions", args);
    }

    [Fact]
    public void BuildCommandArgs_Excludes_DangerouslySkipPermissions_When_False()
    {
        var request = new AgentStartRequest(
            SessionId: "sess-1",
            WorkingDirectory: "/tmp",
            Prompt: "test",
            Permissions: new AgentPermissions(SkipPermissionPrompts: false));

        var args = _adapter.BuildCommandArgs(request);

        Assert.DoesNotContain("--dangerously-skip-permissions", args);
    }

    [Fact]
    public void BuildCommandArgs_Includes_AllowedTools()
    {
        var request = new AgentStartRequest(
            SessionId: "sess-1",
            WorkingDirectory: "/tmp",
            Prompt: "test",
            Permissions: new AgentPermissions(AllowedTools: new[] { "Read", "Write", "Bash" }));

        var args = _adapter.BuildCommandArgs(request);

        Assert.Contains("--allowedTools", args);
        Assert.Contains("Read", args);
        Assert.Contains("Write", args);
        Assert.Contains("Bash", args);
    }

    [Fact]
    public void BuildCommandArgs_Includes_OutputFormat_StreamJson()
    {
        var request = new AgentStartRequest(
            SessionId: "sess-1",
            WorkingDirectory: "/tmp",
            Prompt: "test");

        var args = _adapter.BuildCommandArgs(request);

        var fmtIndex = args.ToList().IndexOf("--output-format");
        Assert.True(fmtIndex >= 0);
        Assert.Equal("stream-json", args[fmtIndex + 1]);
    }

    [Fact]
    public void BuildCommandArgs_Includes_Prompt_Text()
    {
        var request = new AgentStartRequest(
            SessionId: "sess-1",
            WorkingDirectory: "/tmp",
            Prompt: "Fix the bug in main.cs");

        var args = _adapter.BuildCommandArgs(request);

        var pIndex = args.ToList().IndexOf("-p");
        Assert.True(pIndex >= 0);
        Assert.Equal("Fix the bug in main.cs", args[pIndex + 1]);
    }

    [Fact]
    public void AgentConfig_Deserializes_From_Json()
    {
        var json = """
        {
          "agentType": "claude-code",
          "displayName": "Claude Code",
          "cliCommand": "claude",
          "outputFormat": "stream-json",
          "defaultPermissions": {
            "skipPermissionPrompts": false,
            "allowedTools": ["Read", "Write"],
            "disallowedTools": []
          },
          "cliVersionPattern": "claude --version"
        }
        """;

        var config = JsonSerializer.Deserialize<AgentAdapterConfig>(json);

        Assert.NotNull(config);
        Assert.Equal("claude-code", config.AgentType);
        Assert.Equal("Claude Code", config.DisplayName);
        Assert.Equal("claude", config.CliCommand);
        Assert.Equal("stream-json", config.OutputFormat);
        Assert.NotNull(config.DefaultPermissions);
        Assert.False(config.DefaultPermissions.SkipPermissionPrompts);
        Assert.Equal(new[] { "Read", "Write" }, config.DefaultPermissions.AllowedTools);
        Assert.Equal("claude --version", config.CliVersionPattern);
    }

    [Fact]
    public void PermissionMerger_SessionOverrides_Win()
    {
        var sessionPerms = new AgentPermissions(
            SkipPermissionPrompts: true,
            AllowedTools: new[] { "Bash" });

        var agentDefaults = new AgentPermissionConfig
        {
            SkipPermissionPrompts = false,
            AllowedTools = new[] { "Read", "Write" },
            DisallowedTools = new[] { "Bash" }
        };

        var merged = PermissionMerger.Merge(sessionPerms, agentDefaults);

        // Session's SkipPermissionPrompts=true wins (OR logic)
        Assert.True(merged.SkipPermissionPrompts);
        // Session's AllowedTools wins when provided
        Assert.Equal(new[] { "Bash" }, merged.AllowedTools);
        // Session has no DisallowedTools override, so agent default wins
        Assert.Equal(new[] { "Bash" }, merged.DisallowedTools);
    }

    [Fact]
    public void BuildCommandArgs_Includes_NoSessionPersistence()
    {
        var request = new AgentStartRequest(
            SessionId: "sess-1",
            WorkingDirectory: "/tmp",
            Prompt: "test");

        var args = _adapter.BuildCommandArgs(request);

        Assert.Contains("--no-session-persistence", args);
    }

    [Fact]
    public void BuildCommandArgs_With_AgentConfig_Merges_Permissions()
    {
        var config = new AgentAdapterConfig
        {
            AgentType = "claude-code",
            CliCommand = "claude",
            DefaultPermissions = new AgentPermissionConfig
            {
                SkipPermissionPrompts = true,
                AllowedTools = new[] { "Read" }
            }
        };

        var request = new AgentStartRequest(
            SessionId: "sess-1",
            WorkingDirectory: "/tmp",
            Prompt: "test",
            Permissions: null,
            AgentConfig: config);

        var args = _adapter.BuildCommandArgs(request);

        Assert.Contains("--dangerously-skip-permissions", args);
        Assert.Contains("--allowedTools", args);
        Assert.Contains("Read", args);
    }
}
