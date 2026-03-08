using System.Text.Json;
using AgentHub.Orchestration.HostDaemon;
using Xunit;

namespace AgentHub.Tests;

public class HostDaemonProtocolTests
{
    [Fact]
    public void StartSession_Command_Serializes_With_Correct_Command_Field()
    {
        var payload = new StartSessionPayload
        {
            AgentType = "claude-code",
            Prompt = "Fix the bug",
            WorkingDirectory = "/workspace"
        };

        var cmd = HostCommandProtocol.CreateStartSession("sess-1", payload);
        var json = HostCommandProtocol.Serialize(cmd);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("start-session", doc.RootElement.GetProperty("command").GetString());
        Assert.Equal("sess-1", doc.RootElement.GetProperty("sessionId").GetString());
    }

    [Fact]
    public void StopSession_Command_Serializes_With_Correct_Command_Field()
    {
        var cmd = HostCommandProtocol.CreateStopSession("sess-2");
        var json = HostCommandProtocol.Serialize(cmd);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("stop-session", doc.RootElement.GetProperty("command").GetString());
        Assert.Equal("sess-2", doc.RootElement.GetProperty("sessionId").GetString());
    }

    [Fact]
    public void ReportStatus_Command_Serializes_With_Correct_Command_Field()
    {
        var cmd = HostCommandProtocol.CreateReportStatus();
        var json = HostCommandProtocol.Serialize(cmd);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("report-status", doc.RootElement.GetProperty("command").GetString());
    }

    [Fact]
    public void Ping_Command_Serializes_With_Correct_Command_Field()
    {
        var cmd = HostCommandProtocol.CreatePing();
        var json = HostCommandProtocol.Serialize(cmd);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("ping", doc.RootElement.GetProperty("command").GetString());
    }

    [Fact]
    public void Response_Deserializes_Success()
    {
        var json = """{"success":true,"command":"start-session","sessionId":"sess-1"}""";

        var response = HostCommandProtocol.DeserializeResponse(json);

        Assert.True(response.Success);
        Assert.Equal("start-session", response.Command);
        Assert.Equal("sess-1", response.SessionId);
        Assert.Null(response.Error);
    }

    [Fact]
    public void Response_Deserializes_Error()
    {
        var json = """{"success":false,"command":"start-session","error":"Agent not found"}""";

        var response = HostCommandProtocol.DeserializeResponse(json);

        Assert.False(response.Success);
        Assert.Equal("start-session", response.Command);
        Assert.Equal("Agent not found", response.Error);
    }

    [Fact]
    public void RoundTrip_Serialization_Preserves_Data()
    {
        var payload = new StartSessionPayload
        {
            AgentType = "claude-code",
            Prompt = "Run tests",
            WorkingDirectory = "/home/user/project",
            Environment = new Dictionary<string, string> { ["CI"] = "true" },
            Permissions = new PermissionPayload
            {
                SkipPermissionPrompts = true,
                AllowedTools = new[] { "Read", "Bash" }
            }
        };

        var cmd = HostCommandProtocol.CreateStartSession("sess-rt", payload);
        var json = HostCommandProtocol.Serialize(cmd);

        // Verify it's valid JSON we can re-parse
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("start-session", root.GetProperty("command").GetString());
        Assert.Equal("1.0", root.GetProperty("version").GetString());
        Assert.Equal("sess-rt", root.GetProperty("sessionId").GetString());

        // Verify payload content survived round-trip
        var payloadEl = root.GetProperty("payload");
        Assert.Equal("claude-code", payloadEl.GetProperty("agentType").GetString());
        Assert.Equal("Run tests", payloadEl.GetProperty("prompt").GetString());
        Assert.Equal("/home/user/project", payloadEl.GetProperty("workingDirectory").GetString());
        Assert.Equal("true", payloadEl.GetProperty("environment").GetProperty("CI").GetString());
        Assert.True(payloadEl.GetProperty("permissions").GetProperty("skipPermissionPrompts").GetBoolean());
    }

    [Fact]
    public void Protocol_Version_Present_In_All_Commands()
    {
        var commands = new[]
        {
            HostCommandProtocol.CreateStartSession("s1", new StartSessionPayload { AgentType = "test" }),
            HostCommandProtocol.CreateStopSession("s2"),
            HostCommandProtocol.CreateReportStatus(),
            HostCommandProtocol.CreatePing()
        };

        foreach (var cmd in commands)
        {
            var json = HostCommandProtocol.Serialize(cmd);
            using var doc = JsonDocument.Parse(json);
            Assert.Equal("1.0", doc.RootElement.GetProperty("version").GetString());
        }
    }

    [Fact]
    public void Serialized_Commands_Are_SingleLine()
    {
        var payload = new StartSessionPayload
        {
            AgentType = "claude-code",
            Prompt = "multi\nline\nprompt",
            WorkingDirectory = "/workspace"
        };

        var cmd = HostCommandProtocol.CreateStartSession("sess-sl", payload);
        var json = HostCommandProtocol.Serialize(cmd);

        // JSON should not contain literal newlines (they should be escaped as \n)
        Assert.DoesNotContain("\n", json);
        Assert.DoesNotContain("\r", json);
    }

    [Fact]
    public void Response_With_Data_Element_Deserializes()
    {
        var json = """{"success":true,"command":"report-status","data":{"hostId":"host-1","activeSessions":3,"cpuPercent":45.2}}""";

        var response = HostCommandProtocol.DeserializeResponse(json);

        Assert.True(response.Success);
        Assert.Equal("report-status", response.Command);
        Assert.NotNull(response.Data);
        Assert.Equal("host-1", response.Data.Value.GetProperty("hostId").GetString());
        Assert.Equal(3, response.Data.Value.GetProperty("activeSessions").GetInt32());
    }
}
