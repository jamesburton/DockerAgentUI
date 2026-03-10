using AgentHub.Contracts;
using AgentHub.Orchestration;
using AgentHub.Orchestration.Backends;
using AgentHub.Orchestration.Coordinator;
using AgentHub.Orchestration.Data;
using AgentHub.Orchestration.Data.Entities;
using AgentHub.Orchestration.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgentHub.Tests;

public class SessionCoordinatorTests
{
    private static AgentHubDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AgentHubDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AgentHubDbContext(options);
    }

    private static IDbContextFactory<AgentHubDbContext> CreateFactory(string dbName)
        => new TestDbContextFactory(dbName);

    private static SessionCoordinator CreateCoordinator(
        IEnumerable<ISessionBackend>? backends = null,
        IDbContextFactory<AgentHubDbContext>? dbFactory = null)
    {
        backends ??= [new InMemoryBackend()];
        dbFactory ??= CreateFactory("default-coord-db");

        return new SessionCoordinator(
            backends,
            new SimplePlacement(),
            new BasicSanitizationService(),
            new EmptySkillRegistry(),
            new AllowAllPolicy(),
            new ApprovalService(dbFactory, NullLogger<ApprovalService>.Instance),
            dbFactory);
    }

    // --- StopSessionAsync forceKill routing ---

    [Fact]
    public async Task StopSessionAsync_WithForceKillFalse_CallsBackendStopWithForceFalse()
    {
        var backend = new TrackingBackend();
        var coordinator = CreateCoordinator(backends: [backend]);
        var events = new List<SessionEvent>();

        var sessionId = await coordinator.StartSessionAsync("user-1",
            new StartSessionRequest("test-image", new SessionRequirements()),
            e => { events.Add(e); return Task.CompletedTask; },
            CancellationToken.None);

        await coordinator.StopSessionAsync("user-1", sessionId, forceKill: false, CancellationToken.None);

        Assert.False(backend.LastForceKill);
    }

    [Fact]
    public async Task StopSessionAsync_WithForceKillTrue_CallsBackendStopWithForceTrue()
    {
        var backend = new TrackingBackend();
        var coordinator = CreateCoordinator(backends: [backend]);
        var events = new List<SessionEvent>();

        var sessionId = await coordinator.StartSessionAsync("user-1",
            new StartSessionRequest("test-image", new SessionRequirements()),
            e => { events.Add(e); return Task.CompletedTask; },
            CancellationToken.None);

        await coordinator.StopSessionAsync("user-1", sessionId, forceKill: true, CancellationToken.None);

        Assert.True(backend.LastForceKill);
    }

    // --- ListSessionsAsync aggregation ---

    [Fact]
    public async Task ListSessionsAsync_AggregatesFromMultipleBackends_OrderedByCreatedUtcDesc()
    {
        var backend1 = new InMemoryBackend();
        var backend2 = new TrackingBackend();
        var coordinator = CreateCoordinator(backends: [backend1, backend2]);
        var events = new List<SessionEvent>();

        // Start session on backend2 (it will have the tracking backend name)
        var sid = await coordinator.StartSessionAsync("user-1",
            new StartSessionRequest("test-image", new SessionRequirements()),
            e => { events.Add(e); return Task.CompletedTask; },
            CancellationToken.None);

        var sessions = await coordinator.ListSessionsAsync("user-1", CancellationToken.None);

        Assert.Single(sessions);
        Assert.Equal(sid, sessions[0].SessionId);
    }

    // --- StartSessionAsync prompt resolution ---

    [Fact]
    public async Task StartSessionAsync_PersistsPromptFromRequest()
    {
        var backend = new TrackingBackend();
        var coordinator = CreateCoordinator(backends: [backend]);
        var events = new List<SessionEvent>();

        await coordinator.StartSessionAsync("user-1",
            new StartSessionRequest("test-image", new SessionRequirements(), Prompt: "Do the thing"),
            e => { events.Add(e); return Task.CompletedTask; },
            CancellationToken.None);

        Assert.Equal("Do the thing", backend.LastPrompt);
    }

    [Fact]
    public async Task StartSessionAsync_WithNullPrompt_FallsBackToReason()
    {
        var backend = new TrackingBackend();
        var coordinator = CreateCoordinator(backends: [backend]);
        var events = new List<SessionEvent>();

        await coordinator.StartSessionAsync("user-1",
            new StartSessionRequest("test-image", new SessionRequirements(), Reason: "fix bug"),
            e => { events.Add(e); return Task.CompletedTask; },
            CancellationToken.None);

        Assert.Equal("fix bug", backend.LastPrompt);
    }

    // --- GetSessionHistoryAsync pagination ---

    [Fact]
    public async Task GetSessionHistoryAsync_ReturnsPaginatedSessions()
    {
        var dbName = nameof(GetSessionHistoryAsync_ReturnsPaginatedSessions);
        var dbFactory = CreateFactory(dbName);
        var coordinator = CreateCoordinator(dbFactory: dbFactory);

        // Seed sessions in DB
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            for (int i = 0; i < 5; i++)
            {
                db.Sessions.Add(new SessionEntity
                {
                    SessionId = $"sess-{i:D3}",
                    OwnerUserId = "user-1",
                    State = i % 2 == 0 ? SessionState.Running : SessionState.Stopped,
                    CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-i),
                    Backend = "test",
                    RequirementsJson = "{}"
                });
            }
            await db.SaveChangesAsync();
        }

        var (items, total) = await coordinator.GetSessionHistoryAsync("user-1", skip: 1, take: 2, stateFilter: null, CancellationToken.None);

        Assert.Equal(5, total);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task GetSessionHistoryAsync_WithStateFilter_ReturnsOnlyMatchingState()
    {
        var dbName = nameof(GetSessionHistoryAsync_WithStateFilter_ReturnsOnlyMatchingState);
        var dbFactory = CreateFactory(dbName);
        var coordinator = CreateCoordinator(dbFactory: dbFactory);

        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            db.Sessions.Add(new SessionEntity
            {
                SessionId = "running-1", OwnerUserId = "user-1", State = SessionState.Running,
                CreatedUtc = DateTimeOffset.UtcNow, Backend = "test", RequirementsJson = "{}"
            });
            db.Sessions.Add(new SessionEntity
            {
                SessionId = "stopped-1", OwnerUserId = "user-1", State = SessionState.Stopped,
                CreatedUtc = DateTimeOffset.UtcNow, Backend = "test", RequirementsJson = "{}"
            });
            await db.SaveChangesAsync();
        }

        var (items, total) = await coordinator.GetSessionHistoryAsync("user-1", skip: 0, take: 10, stateFilter: "Stopped", CancellationToken.None);

        Assert.Equal(1, total);
        Assert.Single(items);
        Assert.Equal(SessionState.Stopped, items[0].State);
    }
}

// --- Test helpers ---

file sealed class TestDbContextFactory(string dbName) : IDbContextFactory<AgentHubDbContext>
{
    public AgentHubDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AgentHubDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AgentHubDbContext(options);
    }

    public Task<AgentHubDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(CreateDbContext());
}

/// <summary>
/// Backend that tracks calls for assertion in tests.
/// </summary>
file sealed class TrackingBackend : ISessionBackend
{
    public string Name => "tracking";
    private readonly Dictionary<string, SessionSummary> _sessions = new();

    public bool? LastForceKill { get; private set; }
    public string? LastPrompt { get; private set; }

    public bool CanHandle(SessionRequirements requirements, NodeCapability node) => true;

    public Task<IReadOnlyList<NodeCapability>> GetInventoryAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<NodeCapability>>([
            new NodeCapability("track-node-01", Name, "linux", 8, 16384, false, false,
                new Dictionary<string, string> { ["tier"] = "dev" })
        ]);

    public Task<string> StartAsync(string ownerUserId, StartSessionRequest request, PlacementDecision placement,
        Func<SessionEvent, Task> emit, CancellationToken ct)
    {
        var id = $"track_{Guid.NewGuid():N}";
        LastPrompt = request.Prompt ?? request.Reason ?? string.Empty;
        _sessions[id] = new SessionSummary(id, ownerUserId, SessionState.Running, DateTimeOffset.UtcNow,
            Name, placement.NodeId, request.Requirements);
        return Task.FromResult(id);
    }

    public Task<bool> SendInputAsync(string sessionId, SendInputRequest request, CancellationToken ct) => Task.FromResult(true);

    public Task StopAsync(string sessionId, bool forceKill, CancellationToken ct)
    {
        LastForceKill = forceKill;
        if (_sessions.TryGetValue(sessionId, out var s))
            _sessions[sessionId] = s with { State = SessionState.Stopped };
        return Task.CompletedTask;
    }

    public Task<SessionSummary?> GetAsync(string sessionId, CancellationToken ct)
        => Task.FromResult(_sessions.TryGetValue(sessionId, out var s) ? s : null);

    public Task<IReadOnlyList<SessionSummary>> ListAsync(string ownerUserId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<SessionSummary>>(
            _sessions.Values.Where(s => s.OwnerUserId == ownerUserId).ToArray());
}

/// <summary>Simple placement that always picks the first node.</summary>
file sealed class SimplePlacement : IPlacementEngine
{
    public PlacementDecision ChooseNode(string ownerUserId, SessionRequirements requirements, IReadOnlyList<NodeCapability> inventory)
        => new(inventory[0].Backend, inventory[0].NodeId);
}

/// <summary>Empty skill registry for coordinator tests.</summary>
file sealed class EmptySkillRegistry : ISkillRegistry
{
    public IReadOnlyList<SkillManifest> GetAll() => [];
    public SkillManifest? TryGet(string skillId) => null;
}

/// <summary>Policy that allows everything.</summary>
file sealed class AllowAllPolicy : ISkillPolicyService
{
    public PolicySnapshot GetPolicySnapshot() => new("allow-all", DateTimeOffset.UtcNow, [], [], []);
    public bool IsAllowed(string? skillId, SessionSummary session, bool elevated) => true;
}
