using AgentHub.Contracts;
using AgentHub.Orchestration;
using AgentHub.Orchestration.Config;
using AgentHub.Orchestration.Coordinator;
using AgentHub.Orchestration.Data;
using AgentHub.Orchestration.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AgentHub.Tests;

public class SessionCoordinatorApprovalTests
{
    // Shared tracking list: tests check whether backend.SendInputAsync was reached
    private readonly List<string> _backendCalls = [];

    private IDbContextFactory<AgentHubDbContext> CreateFactory()
    {
        var options = new DbContextOptionsBuilder<AgentHubDbContext>()
            .UseInMemoryDatabase($"approval-test-{Guid.NewGuid():N}")
            .Options;
        return new TestDbContextFactory(options);
    }

    private async Task<(SessionCoordinator coordinator, string sessionId)> SetupWithSession(
        ISanitizationService sanitizer,
        ApprovalService approvalService,
        IDbContextFactory<AgentHubDbContext> dbFactory,
        bool acceptRisk = false)
    {
        var backend = new ApprovalTestBackend(_backendCalls, acceptRisk);

        var coordinator = new SessionCoordinator(
            [backend],
            new ApprovalTestPlacement(),
            sanitizer,
            new ApprovalTestSkillRegistry(),
            new ApprovalTestAllowAllPolicy(),
            approvalService,
            dbFactory,
            Options.Create(new CoordinationOptions()));

        var sessionId = await coordinator.StartSessionAsync("user-1",
            new StartSessionRequest("test-image", new SessionRequirements(AcceptRisk: acceptRisk)),
            _ => Task.CompletedTask, CancellationToken.None);

        return (coordinator, sessionId);
    }

    [Fact]
    public async Task SendInputAsync_PromptTier_CallsRequestApprovalAsync_BlocksUntilApproved()
    {
        var dbFactory = CreateFactory();
        var approvalService = new ApprovalService(dbFactory, NullLogger<ApprovalService>.Instance);
        var sanitizer = new ApprovalTestSanitizer(TrustTier.Prompt);

        var (coordinator, sessionId) = await SetupWithSession(sanitizer, approvalService, dbFactory);
        var events = new List<SessionEvent>();

        // Auto-resolve the approval after a short delay
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            var pending = approvalService.GetPendingApprovalIds();
            foreach (var id in pending)
                approvalService.ResolveApproval(id, true, "tester");
        });

        await coordinator.SendInputAsync("user-1", sessionId,
            new SendInputRequest("hello"),
            e => { events.Add(e); return Task.CompletedTask; },
            CancellationToken.None);

        // Approval request event should have been emitted
        Assert.Contains(events, e => e.Kind == SessionEventKind.ApprovalRequest);
        // Backend should have been called after approval
        Assert.Contains(_backendCalls, c => c == "SendInput");
    }

    [Fact]
    public async Task SendInputAsync_AlwaysDenyTier_EmitsPolicyEvent_Throws()
    {
        var dbFactory = CreateFactory();
        var approvalService = new ApprovalService(dbFactory, NullLogger<ApprovalService>.Instance);
        var sanitizer = new ApprovalTestSanitizer(TrustTier.AlwaysDeny);

        var (coordinator, sessionId) = await SetupWithSession(sanitizer, approvalService, dbFactory);
        var events = new List<SessionEvent>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.SendInputAsync("user-1", sessionId,
                new SendInputRequest("hello"),
                e => { events.Add(e); return Task.CompletedTask; },
                CancellationToken.None));

        Assert.Contains("denied by trust tier", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(events, e => e.Kind == SessionEventKind.Policy);
    }

    [Fact]
    public async Task SendInputAsync_AlwaysAllowTier_SkipsApproval_ProceedsToBackend()
    {
        var dbFactory = CreateFactory();
        var approvalService = new ApprovalService(dbFactory, NullLogger<ApprovalService>.Instance);
        var sanitizer = new ApprovalTestSanitizer(TrustTier.AlwaysAllow);

        var (coordinator, sessionId) = await SetupWithSession(sanitizer, approvalService, dbFactory);
        var events = new List<SessionEvent>();

        await coordinator.SendInputAsync("user-1", sessionId,
            new SendInputRequest("hello"),
            e => { events.Add(e); return Task.CompletedTask; },
            CancellationToken.None);

        // No approval request event
        Assert.DoesNotContain(events, e => e.Kind == SessionEventKind.ApprovalRequest);
        // Backend was called
        Assert.Contains(_backendCalls, c => c == "SendInput");
    }

    [Fact]
    public async Task SendInputAsync_SkipPermissionPrompts_AutoApproves_NoBlocking()
    {
        var dbFactory = CreateFactory();
        var approvalService = new ApprovalService(dbFactory, NullLogger<ApprovalService>.Instance);
        var sanitizer = new ApprovalTestSanitizer(TrustTier.Prompt);

        var (coordinator, sessionId) = await SetupWithSession(sanitizer, approvalService, dbFactory, acceptRisk: true);
        var events = new List<SessionEvent>();

        await coordinator.SendInputAsync("user-1", sessionId,
            new SendInputRequest("hello"),
            e => { events.Add(e); return Task.CompletedTask; },
            CancellationToken.None);

        // Should auto-approve, no blocking, no approval request event (SkipPermissionPrompts path)
        Assert.DoesNotContain(events, e => e.Kind == SessionEventKind.ApprovalRequest);
        Assert.Contains(_backendCalls, c => c == "SendInput");
    }

    [Fact]
    public async Task SendInputAsync_ApprovalDenied_EmitsPolicyEvent_Throws()
    {
        var dbFactory = CreateFactory();
        var approvalService = new ApprovalService(dbFactory, NullLogger<ApprovalService>.Instance);
        var sanitizer = new ApprovalTestSanitizer(TrustTier.Prompt);

        var (coordinator, sessionId) = await SetupWithSession(sanitizer, approvalService, dbFactory);
        var events = new List<SessionEvent>();

        // Auto-deny the approval
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            var pending = approvalService.GetPendingApprovalIds();
            foreach (var id in pending)
                approvalService.ResolveApproval(id, false, "tester");
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.SendInputAsync("user-1", sessionId,
                new SendInputRequest("hello"),
                e => { events.Add(e); return Task.CompletedTask; },
                CancellationToken.None));

        Assert.Contains("denied", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(events, e => e.Kind == SessionEventKind.Policy);
    }
}

// --- Test helpers for approval tests (non-file-local to avoid CS9051) ---

internal sealed class ApprovalTestSanitizer(TrustTier tier) : ISanitizationService
{
    public SanitizationDecision Evaluate(SendInputRequest request, SessionSummary session, SkillManifest? skill)
        => new(true, request.Input, RiskLevel.Low, ["Input accepted"]);

    public TrustTierDecision EvaluateWithTrustTier(string action, ScopedPolicyConfig? policy)
    {
        return tier switch
        {
            TrustTier.AlwaysAllow => new TrustTierDecision(TrustTier.AlwaysAllow, RequiresApproval: false, Denied: false),
            TrustTier.AlwaysDeny => new TrustTierDecision(TrustTier.AlwaysDeny, RequiresApproval: false, Denied: true),
            TrustTier.Prompt => new TrustTierDecision(TrustTier.Prompt, RequiresApproval: true, Denied: false),
            _ => new TrustTierDecision(TrustTier.Prompt, RequiresApproval: true, Denied: false)
        };
    }
}

internal sealed class ApprovalTestBackend(List<string> calls, bool acceptRisk = false) : ISessionBackend
{
    private readonly Dictionary<string, SessionSummary> _sessions = new();

    public string Name => "approval-test";

    public bool CanHandle(SessionRequirements requirements, NodeCapability node) => true;

    public Task<IReadOnlyList<NodeCapability>> GetInventoryAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<NodeCapability>>([
            new NodeCapability("test-node-01", Name, "linux", 8, 16384, false, false,
                new Dictionary<string, string> { ["tier"] = "dev" })
        ]);

    public Task<string> StartAsync(string ownerUserId, StartSessionRequest request, PlacementDecision placement,
        Func<SessionEvent, Task> emit, CancellationToken ct)
    {
        var id = $"at_{Guid.NewGuid():N}";
        var reqs = acceptRisk ? request.Requirements with { AcceptRisk = true } : request.Requirements;
        _sessions[id] = new SessionSummary(id, ownerUserId, SessionState.Running, DateTimeOffset.UtcNow,
            Name, placement.NodeId, reqs);
        return Task.FromResult(id);
    }

    public Task<bool> SendInputAsync(string sessionId, SendInputRequest request, CancellationToken ct)
    {
        calls.Add("SendInput");
        return Task.FromResult(true);
    }

    public Task StopAsync(string sessionId, bool forceKill, CancellationToken ct) => Task.CompletedTask;

    public Task<SessionSummary?> GetAsync(string sessionId, CancellationToken ct)
        => Task.FromResult(_sessions.TryGetValue(sessionId, out var s) ? s : null);

    public Task<IReadOnlyList<SessionSummary>> ListAsync(string ownerUserId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<SessionSummary>>(
            _sessions.Values.Where(s => s.OwnerUserId == ownerUserId).ToArray());
}

internal sealed class ApprovalTestPlacement : IPlacementEngine
{
    public PlacementDecision ChooseNode(string ownerUserId, SessionRequirements requirements, IReadOnlyList<NodeCapability> inventory)
        => new(inventory[0].Backend, inventory[0].NodeId);
}

internal sealed class ApprovalTestSkillRegistry : ISkillRegistry
{
    public IReadOnlyList<SkillManifest> GetAll() => [];
    public SkillManifest? TryGet(string skillId) => null;
}

internal sealed class ApprovalTestAllowAllPolicy : ISkillPolicyService
{
    public PolicySnapshot GetPolicySnapshot() => new("allow-all", DateTimeOffset.UtcNow, [], [], []);
    public bool IsAllowed(string? skillId, SessionSummary session, bool elevated) => true;
}
