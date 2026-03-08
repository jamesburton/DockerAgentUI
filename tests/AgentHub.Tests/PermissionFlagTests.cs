using System.Text.Json;
using AgentHub.Contracts;
using AgentHub.Orchestration.Config;
using AgentHub.Orchestration.Data;
using AgentHub.Orchestration.Data.Entities;
using AgentHub.Orchestration.HostDaemon;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgentHub.Tests;

public class PermissionFlagTests
{
    // --- SessionEventKind new values ---

    [Theory]
    [InlineData("ApprovalRequest")]
    [InlineData("ApprovalResponse")]
    [InlineData("Heartbeat")]
    [InlineData("SessionCompleted")]
    [InlineData("CleanupStarted")]
    [InlineData("CleanupCompleted")]
    public void SessionEventKind_Has_Phase2_Values(string valueName)
    {
        Assert.True(Enum.TryParse<SessionEventKind>(valueName, out _));
    }

    // --- SessionEntity Phase 2 properties ---

    [Fact]
    public void SessionEntity_Has_Phase2_Properties()
    {
        var entity = new SessionEntity
        {
            SessionId = "test-1",
            CompletedUtc = DateTimeOffset.UtcNow,
            ExitCode = 0,
            CleanupState = "completed",
            IsFireAndForget = true,
            Prompt = "Build the app",
            TimeLimit = "00:30:00",
            CleanupPolicy = "auto"
        };

        Assert.NotNull(entity.CompletedUtc);
        Assert.Equal(0, entity.ExitCode);
        Assert.Equal("completed", entity.CleanupState);
        Assert.True(entity.IsFireAndForget);
        Assert.Equal("Build the app", entity.Prompt);
        Assert.Equal("00:30:00", entity.TimeLimit);
        Assert.Equal("auto", entity.CleanupPolicy);
    }

    // --- ApprovalEntity round-trip through DbContext ---

    [Fact]
    public async Task ApprovalEntity_Persists_RoundTrip_Through_DbContext()
    {
        var options = new DbContextOptionsBuilder<AgentHubDbContext>()
            .UseInMemoryDatabase(databaseName: $"ApprovalTest_{Guid.NewGuid()}")
            .Options;

        var sessionId = "sess-approval-1";
        var approvalId = "appr-1";
        var requestedUtc = DateTimeOffset.UtcNow;

        await using (var ctx = new AgentHubDbContext(options))
        {
            ctx.Sessions.Add(new SessionEntity
            {
                SessionId = sessionId,
                OwnerUserId = "user1",
                State = SessionState.Running,
                CreatedUtc = DateTimeOffset.UtcNow,
                Backend = "ssh"
            });

            ctx.Approvals.Add(new ApprovalEntity
            {
                ApprovalId = approvalId,
                SessionId = sessionId,
                Action = "rm -rf /tmp/test",
                Context = """{"command":"rm -rf /tmp/test","filePath":"/tmp/test"}""",
                Status = "pending",
                RequestedUtc = requestedUtc,
                TimeoutSeconds = 30,
                TimeoutAction = "stop"
            });

            await ctx.SaveChangesAsync();
        }

        await using (var ctx = new AgentHubDbContext(options))
        {
            var approval = await ctx.Approvals.FindAsync(approvalId);
            Assert.NotNull(approval);
            Assert.Equal(sessionId, approval.SessionId);
            Assert.Equal("rm -rf /tmp/test", approval.Action);
            Assert.Equal("pending", approval.Status);
            Assert.Equal(30, approval.TimeoutSeconds);
            Assert.Equal("stop", approval.TimeoutAction);
            Assert.Null(approval.ResolvedUtc);
            Assert.Null(approval.ResolvedBy);
        }
    }

    // --- HostCommandProtocol ForceKill ---

    [Fact]
    public void HostCommandProtocol_CreateForceKill_Produces_Correct_Json()
    {
        var cmd = HostCommandProtocol.CreateForceKill("sess-42");
        var json = HostCommandProtocol.Serialize(cmd);
        var parsed = JsonSerializer.Deserialize<JsonElement>(json);

        Assert.Equal("force-kill", parsed.GetProperty("command").GetString());
        Assert.Equal("sess-42", parsed.GetProperty("sessionId").GetString());
    }

    // --- HostCommand constants ---

    [Fact]
    public void HostCommand_Has_ForceKill_And_ApprovalResponse_Constants()
    {
        Assert.Equal("force-kill", HostCommand.ForceKill);
        Assert.Equal("approval-response", HostCommand.ApprovalResponse);
    }

    // --- StartSessionRequest has Prompt and IsFireAndForget ---

    [Fact]
    public void StartSessionRequest_Has_Prompt_And_IsFireAndForget()
    {
        var req = new StartSessionRequest(
            ImageOrProfile: "claude-code",
            Requirements: new SessionRequirements(),
            Prompt: "Build the feature",
            IsFireAndForget: true);

        Assert.Equal("Build the feature", req.Prompt);
        Assert.True(req.IsFireAndForget);
    }

    // --- PermissionPayload serialization ---

    [Fact]
    public void PermissionPayload_SkipPermissionPrompts_Serializes_Through_Protocol()
    {
        var payload = new StartSessionPayload
        {
            AgentType = "claude-code",
            Prompt = "test prompt",
            WorkingDirectory = "/workspace",
            Permissions = new PermissionPayload
            {
                SkipPermissionPrompts = true,
                AllowedTools = ["Read", "Glob"],
                DisallowedTools = ["Bash"]
            }
        };

        var cmd = HostCommandProtocol.CreateStartSession("sess-perm", payload);
        var json = HostCommandProtocol.Serialize(cmd);
        var parsed = JsonSerializer.Deserialize<JsonElement>(json);

        var permPayload = parsed.GetProperty("payload").GetProperty("permissions");
        Assert.True(permPayload.GetProperty("skipPermissionPrompts").GetBoolean());
        Assert.Equal("Read", permPayload.GetProperty("allowedTools")[0].GetString());
        Assert.Equal("Bash", permPayload.GetProperty("disallowedTools")[0].GetString());
    }

    // --- HostCommandProtocol ApprovalResponse ---

    [Fact]
    public void HostCommandProtocol_CreateApprovalResponse_Produces_Correct_Json()
    {
        var cmd = HostCommandProtocol.CreateApprovalResponse("sess-99", "appr-1", true);
        var json = HostCommandProtocol.Serialize(cmd);
        var parsed = JsonSerializer.Deserialize<JsonElement>(json);

        Assert.Equal("approval-response", parsed.GetProperty("command").GetString());
        Assert.Equal("sess-99", parsed.GetProperty("sessionId").GetString());

        var responsePayload = parsed.GetProperty("payload");
        Assert.Equal("appr-1", responsePayload.GetProperty("approvalId").GetString());
        Assert.True(responsePayload.GetProperty("approved").GetBoolean());
    }

    // --- TrustTier enum ---

    [Theory]
    [InlineData("AlwaysAllow")]
    [InlineData("Prompt")]
    [InlineData("AlwaysDeny")]
    public void TrustTier_Has_Expected_Values(string valueName)
    {
        Assert.True(Enum.TryParse<TrustTier>(valueName, out _));
    }

    // --- ScopedPolicyConfig creation ---

    [Fact]
    public void ScopedPolicyConfig_Can_Be_Created_With_TrustTiers()
    {
        var config = new ScopedPolicyConfig
        {
            Name = "test-policy",
            Scope = "project",
            ScopeId = "my-project",
            TrustTiers = new Dictionary<string, TrustTier>
            {
                ["Read"] = TrustTier.AlwaysAllow,
                ["Write"] = TrustTier.Prompt,
                ["rm -rf /"] = TrustTier.AlwaysDeny
            },
            EnabledSkills = ["code-review", "test-gen"],
            DisabledSkills = ["deploy"],
            ElevatedSkills = ["admin-ops"],
            DefaultTimeoutSeconds = 60,
            DefaultTimeoutAction = "stop"
        };

        Assert.Equal("test-policy", config.Name);
        Assert.Equal("project", config.Scope);
        Assert.Equal(TrustTier.AlwaysAllow, config.TrustTiers["Read"]);
        Assert.Equal(TrustTier.Prompt, config.TrustTiers["Write"]);
        Assert.Equal(TrustTier.AlwaysDeny, config.TrustTiers["rm -rf /"]);
        Assert.Equal(2, config.EnabledSkills.Length);
        Assert.Equal(60, config.DefaultTimeoutSeconds);
    }

    // --- ForceKill payload ---

    [Fact]
    public void ForceKillPayload_Serializes_Correctly()
    {
        var payload = new ForceKillPayload { Reason = "User requested" };
        var json = JsonSerializer.Serialize(payload);
        var parsed = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal("User requested", parsed.GetProperty("reason").GetString());
    }

    // --- ApprovalResponsePayload ---

    [Fact]
    public void ApprovalResponsePayload_Serializes_Correctly()
    {
        var payload = new ApprovalResponsePayload
        {
            ApprovalId = "appr-5",
            Approved = false,
            Reason = "Too risky"
        };
        var json = JsonSerializer.Serialize(payload);
        var parsed = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal("appr-5", parsed.GetProperty("approvalId").GetString());
        Assert.False(parsed.GetProperty("approved").GetBoolean());
        Assert.Equal("Too risky", parsed.GetProperty("reason").GetString());
    }
}
