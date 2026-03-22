using System.Text.Json;
using AgentHub.Contracts;
using AgentHub.Orchestration;
using AgentHub.Orchestration.Config;
using AgentHub.Orchestration.Coordinator;
using AgentHub.Orchestration.Data;
using AgentHub.Orchestration.HostDaemon;
using AgentHub.Orchestration.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AgentHub.Tests;

/// <summary>
/// Tests for steering contracts: IsFollowUp on SendInputRequest,
/// SteeringInput/SteeringDelivered event kinds, and HostCommandProtocol.CreateSendInput.
/// </summary>
public class SteeringContractTests
{
    [Fact]
    public void SendInputRequest_DefaultIsFollowUp_IsFalse()
    {
        // Backward compat: existing callers use positional "new SendInputRequest(text)"
        var request = new SendInputRequest("hello");
        Assert.False(request.IsFollowUp);
    }

    [Fact]
    public void SendInputRequest_WithIsFollowUp_True()
    {
        var request = new SendInputRequest("do this", IsFollowUp: true);
        Assert.True(request.IsFollowUp);
        Assert.Equal("do this", request.Input);
    }

    [Fact]
    public void SendInputRequest_IsFollowUp_SerializesCorrectly()
    {
        var request = new SendInputRequest("test input", IsFollowUp: true);
        var json = JsonSerializer.Serialize(request);
        Assert.Contains("\"IsFollowUp\":true", json);
    }

    [Fact]
    public void SessionEventKind_SteeringInput_ExistsAfterHostMetrics()
    {
        var steeringInput = SessionEventKind.SteeringInput;
        var hostMetrics = SessionEventKind.HostMetrics;
        Assert.True((int)steeringInput > (int)hostMetrics,
            "SteeringInput must be appended after HostMetrics");
    }

    [Fact]
    public void SessionEventKind_SteeringDelivered_ExistsAfterSteeringInput()
    {
        var steeringDelivered = SessionEventKind.SteeringDelivered;
        var steeringInput = SessionEventKind.SteeringInput;
        Assert.True((int)steeringDelivered > (int)steeringInput,
            "SteeringDelivered must come after SteeringInput");
    }

    [Fact]
    public void HostCommandProtocol_CreateSendInput_ProducesCorrectCommand()
    {
        var command = HostCommandProtocol.CreateSendInput("sess1", "do this", true);

        Assert.Equal(HostCommand.SendInput, command.Command);
        Assert.Equal("sess1", command.SessionId);
        Assert.NotNull(command.Payload);

        // Verify payload structure
        var payload = command.Payload!.Value;
        Assert.Equal("do this", payload.GetProperty("input").GetString());
        Assert.True(payload.GetProperty("isFollowUp").GetBoolean());
    }

    [Fact]
    public void HostCommandProtocol_CreateSendInput_DefaultIsFollowUpFalse()
    {
        var command = HostCommandProtocol.CreateSendInput("sess1", "hello");

        var payload = command.Payload!.Value;
        Assert.Equal("hello", payload.GetProperty("input").GetString());
        Assert.False(payload.GetProperty("isFollowUp").GetBoolean());
    }

    [Fact]
    public void HostCommandProtocol_CreateSendInput_SerializesAsJson()
    {
        var command = HostCommandProtocol.CreateSendInput("sess1", "test", true);
        var json = HostCommandProtocol.Serialize(command);

        Assert.Contains("\"command\":\"send-input\"", json);
        Assert.Contains("\"sessionId\":\"sess1\"", json);
    }

    [Fact]
    public void HostCommandProtocol_DeserializeResponse_ParsesSuccess()
    {
        var json = """{"success":true,"command":"send-input","sessionId":"sess1"}""";
        var response = HostCommandProtocol.DeserializeResponse(json);

        Assert.True(response.Success);
        Assert.Equal("send-input", response.Command);
        Assert.Equal("sess1", response.SessionId);
    }

    [Fact]
    public void HostCommandProtocol_DeserializeResponse_ParsesFailure()
    {
        var json = """{"success":false,"command":"send-input","sessionId":"sess1","error":"not found"}""";
        var response = HostCommandProtocol.DeserializeResponse(json);

        Assert.False(response.Success);
        Assert.Equal("not found", response.Error);
    }

    [Fact]
    public void HostCommand_SendInput_ConstantExists()
    {
        Assert.Equal("send-input", HostCommand.SendInput);
    }

    [Fact]
    public void SendInputPayload_HasExpectedProperties()
    {
        var payload = new SendInputPayload { Input = "test", IsFollowUp = true };
        Assert.Equal("test", payload.Input);
        Assert.True(payload.IsFollowUp);
    }
}

/// <summary>
/// Tests for the steering pipeline: coordinator event emission,
/// delivery confirmation, and warning on failure.
/// </summary>
public class SteeringPipelineTests
{
    private IDbContextFactory<AgentHubDbContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<AgentHubDbContext>()
            .UseInMemoryDatabase($"steering-test-{Guid.NewGuid():N}")
            .Options;
        return new TestDbContextFactory(options);
    }

    private async Task<(SessionCoordinator coordinator, string sessionId, SteeringTestBackend backend)> SetupWithSession(
        bool backendDelivers = true)
    {
        var dbFactory = CreateFactory();
        var backend = new SteeringTestBackend(backendDelivers);
        var approvalService = new ApprovalService(dbFactory, NullLogger<ApprovalService>.Instance);

        var coordinator = new SessionCoordinator(
            [backend],
            new SteeringTestPlacement(),
            new SteeringTestSanitizer(),
            new SteeringTestSkillRegistry(),
            new SteeringTestAllowAllPolicy(),
            approvalService,
            dbFactory,
            Options.Create(new CoordinationOptions()),
            new ActiveSessionTracker());

        var sessionId = await coordinator.StartSessionAsync("user-1",
            new StartSessionRequest("test-image", new SessionRequirements()),
            _ => Task.CompletedTask, CancellationToken.None);

        return (coordinator, sessionId, backend);
    }

    [Fact]
    public async Task SendInputAsync_IsFollowUpTrue_EmitsSteeringInputBeforeBackendCall()
    {
        var (coordinator, sessionId, _) = await SetupWithSession(backendDelivers: true);
        var events = new List<SessionEvent>();

        await coordinator.SendInputAsync("user-1", sessionId,
            new SendInputRequest("steer me", IsFollowUp: true),
            e => { events.Add(e); return Task.CompletedTask; },
            CancellationToken.None);

        Assert.Contains(events, e => e.Kind == SessionEventKind.SteeringInput);
        var steeringEvent = events.First(e => e.Kind == SessionEventKind.SteeringInput);
        Assert.Equal("steer me", steeringEvent.Data);
        Assert.NotNull(steeringEvent.Meta);
        Assert.Equal("true", steeringEvent.Meta!["isFollowUp"]);
    }

    [Fact]
    public async Task SendInputAsync_IsFollowUpTrue_EmitsSteeringDeliveredOnSuccess()
    {
        var (coordinator, sessionId, _) = await SetupWithSession(backendDelivers: true);
        var events = new List<SessionEvent>();

        await coordinator.SendInputAsync("user-1", sessionId,
            new SendInputRequest("steer me", IsFollowUp: true),
            e => { events.Add(e); return Task.CompletedTask; },
            CancellationToken.None);

        Assert.Contains(events, e => e.Kind == SessionEventKind.SteeringDelivered);
    }

    [Fact]
    public async Task SendInputAsync_IsFollowUpTrue_EmitsWarningWhenBackendReturnsFalse()
    {
        var (coordinator, sessionId, _) = await SetupWithSession(backendDelivers: false);
        var events = new List<SessionEvent>();

        await coordinator.SendInputAsync("user-1", sessionId,
            new SendInputRequest("steer me", IsFollowUp: true),
            e => { events.Add(e); return Task.CompletedTask; },
            CancellationToken.None);

        // Should NOT emit SteeringDelivered
        Assert.DoesNotContain(events, e => e.Kind == SessionEventKind.SteeringDelivered);
        // Should emit a warning Info event
        Assert.Contains(events, e => e.Kind == SessionEventKind.Info
            && e.Data.Contains("unconfirmed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SendInputAsync_IsFollowUpFalse_DoesNotEmitSteeringEvents()
    {
        var (coordinator, sessionId, _) = await SetupWithSession(backendDelivers: true);
        var events = new List<SessionEvent>();

        await coordinator.SendInputAsync("user-1", sessionId,
            new SendInputRequest("regular input", IsFollowUp: false),
            e => { events.Add(e); return Task.CompletedTask; },
            CancellationToken.None);

        Assert.DoesNotContain(events, e => e.Kind == SessionEventKind.SteeringInput);
        Assert.DoesNotContain(events, e => e.Kind == SessionEventKind.SteeringDelivered);
    }

    [Fact]
    public async Task InMemoryBackend_SendInputAsync_ReturnsTrue()
    {
        var backend = new AgentHub.Orchestration.Backends.InMemoryBackend();
        var result = await backend.SendInputAsync("test-session",
            new SendInputRequest("hello"), CancellationToken.None);
        Assert.True(result);
    }
}

// --- Test helpers for steering pipeline tests ---

internal sealed class SteeringTestBackend(bool delivers) : ISessionBackend
{
    private readonly Dictionary<string, SessionSummary> _sessions = new();

    public string Name => "steering-test";

    public bool CanHandle(SessionRequirements requirements, NodeCapability node) => true;

    public Task<IReadOnlyList<NodeCapability>> GetInventoryAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<NodeCapability>>([
            new NodeCapability("test-node-01", Name, "linux", 8, 16384, false, false,
                new Dictionary<string, string> { ["tier"] = "dev" })
        ]);

    public Task<string> StartAsync(string ownerUserId, StartSessionRequest request, PlacementDecision placement,
        Func<SessionEvent, Task> emit, CancellationToken ct)
    {
        var id = $"st_{Guid.NewGuid():N}";
        _sessions[id] = new SessionSummary(id, ownerUserId, SessionState.Running, DateTimeOffset.UtcNow,
            Name, placement.NodeId, request.Requirements);
        return Task.FromResult(id);
    }

    public Task<bool> SendInputAsync(string sessionId, SendInputRequest request, CancellationToken ct)
        => Task.FromResult(delivers);

    public Task StopAsync(string sessionId, bool forceKill, CancellationToken ct) => Task.CompletedTask;

    public Task<SessionSummary?> GetAsync(string sessionId, CancellationToken ct)
        => Task.FromResult(_sessions.TryGetValue(sessionId, out var s) ? s : null);

    public Task<IReadOnlyList<SessionSummary>> ListAsync(string ownerUserId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<SessionSummary>>(
            _sessions.Values.Where(s => s.OwnerUserId == ownerUserId).ToArray());
}

internal sealed class SteeringTestPlacement : IPlacementEngine
{
    public PlacementDecision ChooseNode(string ownerUserId, SessionRequirements requirements, IReadOnlyList<NodeCapability> inventory)
        => new(inventory[0].Backend, inventory[0].NodeId);
}

internal sealed class SteeringTestSanitizer : ISanitizationService
{
    public SanitizationDecision Evaluate(SendInputRequest request, SessionSummary session, SkillManifest? skill)
        => new(true, request.Input, RiskLevel.Low, ["Input accepted"]);

    public TrustTierDecision EvaluateWithTrustTier(string action, ScopedPolicyConfig? policy)
        => new(TrustTier.AlwaysAllow, RequiresApproval: false, Denied: false);
}

internal sealed class SteeringTestSkillRegistry : ISkillRegistry
{
    public IReadOnlyList<SkillManifest> GetAll() => [];
    public SkillManifest? TryGet(string skillId) => null;
}

internal sealed class SteeringTestAllowAllPolicy : ISkillPolicyService
{
    public PolicySnapshot GetPolicySnapshot() => new("allow-all", DateTimeOffset.UtcNow, [], [], []);
    public bool IsAllowed(string? skillId, SessionSummary session, bool elevated) => true;
}
