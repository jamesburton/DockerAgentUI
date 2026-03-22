using AgentHub.Contracts;
using AgentHub.Orchestration;
using AgentHub.Orchestration.Backends;
using AgentHub.Orchestration.Coordinator;
using AgentHub.Orchestration.Data;
using AgentHub.Orchestration.Data.Entities;
using AgentHub.Orchestration.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AgentHub.Tests;

[Trait("Category", "Phase10")]
public class SpawnSessionTests
{
    private static IDbContextFactory<AgentHubDbContext> CreateFactory(string dbName)
        => new SpawnTestDbContextFactory(dbName);

    private static SessionCoordinator CreateCoordinator(
        IDbContextFactory<AgentHubDbContext>? dbFactory = null,
        ActiveSessionTracker? tracker = null,
        CoordinationOptions? coordOptions = null)
    {
        dbFactory ??= CreateFactory($"spawn-{Guid.NewGuid():N}");
        var backend = new SpawnTrackingBackend();
        backend.SetDbFactory(dbFactory);
        tracker ??= new ActiveSessionTracker();
        coordOptions ??= new CoordinationOptions();

        return new SessionCoordinator(
            [backend],
            new SpawnSimplePlacement(),
            new BasicSanitizationService(),
            new SpawnEmptySkillRegistry(),
            new SpawnAllowAllPolicy(),
            new ApprovalService(dbFactory, NullLogger<ApprovalService>.Instance),
            dbFactory,
            Options.Create(coordOptions),
            tracker);
    }

    private static Func<SessionEvent, Task> CaptureEvents(List<SessionEvent> events)
        => e => { events.Add(e); return Task.CompletedTask; };

    [Fact]
    public async Task Spawn_WithParentSessionId_SetsParentFK()
    {
        var dbName = $"spawn-fk-{Guid.NewGuid():N}";
        var dbFactory = CreateFactory(dbName);
        var coordinator = CreateCoordinator(dbFactory: dbFactory);
        var events = new List<SessionEvent>();

        // Create parent session
        var parentId = await coordinator.StartSessionAsync("user-1",
            new StartSessionRequest("test-agent", new SessionRequirements()),
            CaptureEvents(events), CancellationToken.None);

        // Spawn child with parent
        var childId = await coordinator.StartSessionAsync("user-1",
            new StartSessionRequest("test-agent", new SessionRequirements(), ParentSessionId: parentId),
            CaptureEvents(events), CancellationToken.None);

        // Verify FK is set in DB
        await using var db = await dbFactory.CreateDbContextAsync();
        var childEntity = await db.Sessions.FindAsync(childId);
        Assert.NotNull(childEntity);
        Assert.Equal(parentId, childEntity!.ParentSessionId);
    }

    [Fact]
    public async Task Spawn_WithParentSessionId_EmitsChildSpawnedOnParentStream()
    {
        var dbName = $"spawn-event-{Guid.NewGuid():N}";
        var dbFactory = CreateFactory(dbName);
        var coordinator = CreateCoordinator(dbFactory: dbFactory);
        var events = new List<SessionEvent>();

        var parentId = await coordinator.StartSessionAsync("user-1",
            new StartSessionRequest("test-agent", new SessionRequirements()),
            CaptureEvents(events), CancellationToken.None);

        await coordinator.StartSessionAsync("user-1",
            new StartSessionRequest("test-agent", new SessionRequirements(), ParentSessionId: parentId),
            CaptureEvents(events), CancellationToken.None);

        var childSpawnedEvents = events.Where(e =>
            e.Kind == SessionEventKind.ChildSpawned && e.SessionId == parentId).ToList();
        Assert.Single(childSpawnedEvents);
        Assert.Contains("childSessionId", childSpawnedEvents[0].Meta!.Keys);
    }

    [Fact]
    public async Task Spawn_ExceedingDepthLimit_Throws()
    {
        var dbName = $"spawn-depth-{Guid.NewGuid():N}";
        var dbFactory = CreateFactory(dbName);
        var coordinator = CreateCoordinator(
            dbFactory: dbFactory,
            coordOptions: new CoordinationOptions { MaxDepth = 1 });
        var events = new List<SessionEvent>();

        // Create parent (depth 0)
        var parentId = await coordinator.StartSessionAsync("user-1",
            new StartSessionRequest("test-agent", new SessionRequirements()),
            CaptureEvents(events), CancellationToken.None);

        // Create child (depth 1) -- should succeed
        var childId = await coordinator.StartSessionAsync("user-1",
            new StartSessionRequest("test-agent", new SessionRequirements(), ParentSessionId: parentId),
            CaptureEvents(events), CancellationToken.None);

        // Create grandchild (depth 2) -- should fail (MaxDepth = 1)
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.StartSessionAsync("user-1",
                new StartSessionRequest("test-agent", new SessionRequirements(), ParentSessionId: childId),
                CaptureEvents(events), CancellationToken.None));
    }

    [Fact]
    public async Task Spawn_ExceedingChildCountLimit_Throws()
    {
        var dbName = $"spawn-count-{Guid.NewGuid():N}";
        var dbFactory = CreateFactory(dbName);
        var coordinator = CreateCoordinator(
            dbFactory: dbFactory,
            coordOptions: new CoordinationOptions { MaxChildrenPerParent = 1 });
        var events = new List<SessionEvent>();

        var parentId = await coordinator.StartSessionAsync("user-1",
            new StartSessionRequest("test-agent", new SessionRequirements()),
            CaptureEvents(events), CancellationToken.None);

        // First child -- should succeed
        await coordinator.StartSessionAsync("user-1",
            new StartSessionRequest("test-agent", new SessionRequirements(), ParentSessionId: parentId),
            CaptureEvents(events), CancellationToken.None);

        // Second child -- should fail (MaxChildrenPerParent = 1)
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.StartSessionAsync("user-1",
                new StartSessionRequest("test-agent", new SessionRequirements(), ParentSessionId: parentId),
                CaptureEvents(events), CancellationToken.None));
    }

    [Fact]
    public async Task StopSession_WithActiveChildren_EmitsWarning()
    {
        var dbName = $"spawn-orphan-{Guid.NewGuid():N}";
        var dbFactory = CreateFactory(dbName);
        var coordinator = CreateCoordinator(dbFactory: dbFactory);
        var events = new List<SessionEvent>();

        var parentId = await coordinator.StartSessionAsync("user-1",
            new StartSessionRequest("test-agent", new SessionRequirements()),
            CaptureEvents(events), CancellationToken.None);

        await coordinator.StartSessionAsync("user-1",
            new StartSessionRequest("test-agent", new SessionRequirements(), ParentSessionId: parentId),
            CaptureEvents(events), CancellationToken.None);

        // Stop parent while child is still running
        await coordinator.StopSessionAsync("user-1", parentId, CancellationToken.None);

        // Warning is persisted to DB (StopSessionAsync has no emit callback)
        await using var db = await dbFactory.CreateDbContextAsync();
        var warningEvents = await db.Events
            .Where(e => e.SessionId == parentId && e.Kind == SessionEventKind.Info)
            .ToListAsync();
        var orphanWarning = warningEvents.FirstOrDefault(e =>
            e.Data.Contains("child session(s) still active"));
        Assert.NotNull(orphanWarning);
    }

    [Fact]
    public async Task ActiveSessionTracker_IncrementedOnStart_DecrementedOnStop()
    {
        var dbName = $"spawn-tracker-{Guid.NewGuid():N}";
        var dbFactory = CreateFactory(dbName);
        var tracker = new ActiveSessionTracker();
        var coordinator = CreateCoordinator(dbFactory: dbFactory, tracker: tracker);
        var events = new List<SessionEvent>();

        var sessionId = await coordinator.StartSessionAsync("user-1",
            new StartSessionRequest("test-agent", new SessionRequirements()),
            CaptureEvents(events), CancellationToken.None);

        Assert.Equal(1, tracker.GetCount("track-node-01"));

        await coordinator.StopSessionAsync("user-1", sessionId, CancellationToken.None);

        Assert.Equal(0, tracker.GetCount("track-node-01"));
    }

    [Fact]
    public async Task GetSessionHistory_IncludesParentSessionId()
    {
        var dbName = $"spawn-history-{Guid.NewGuid():N}";
        var dbFactory = CreateFactory(dbName);
        var coordinator = CreateCoordinator(dbFactory: dbFactory);
        var events = new List<SessionEvent>();

        var parentId = await coordinator.StartSessionAsync("user-1",
            new StartSessionRequest("test-agent", new SessionRequirements()),
            CaptureEvents(events), CancellationToken.None);

        var childId = await coordinator.StartSessionAsync("user-1",
            new StartSessionRequest("test-agent", new SessionRequirements(), ParentSessionId: parentId),
            CaptureEvents(events), CancellationToken.None);

        var (items, _) = await coordinator.GetSessionHistoryAsync("user-1", 0, 50, null, CancellationToken.None);
        var childSummary = items.FirstOrDefault(i => i.SessionId == childId);
        Assert.NotNull(childSummary);
        Assert.Equal(parentId, childSummary!.ParentSessionId);
    }
}

// --- Test helpers (file-scoped to avoid conflicts) ---

file sealed class SpawnTestDbContextFactory(string dbName) : IDbContextFactory<AgentHubDbContext>
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

file sealed class SpawnTrackingBackend : ISessionBackend
{
    public string Name => "tracking";
    private readonly Dictionary<string, SessionSummary> _sessions = new();
    private IDbContextFactory<AgentHubDbContext>? _dbFactory;

    public void SetDbFactory(IDbContextFactory<AgentHubDbContext> dbFactory) => _dbFactory = dbFactory;

    public bool CanHandle(SessionRequirements requirements, NodeCapability node) => true;

    public Task<IReadOnlyList<NodeCapability>> GetInventoryAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<NodeCapability>>([
            new NodeCapability("track-node-01", Name, "linux", 8, 16384, false, false,
                new Dictionary<string, string> { ["tier"] = "dev" })
        ]);

    public async Task<string> StartAsync(string ownerUserId, StartSessionRequest request, PlacementDecision placement,
        Func<SessionEvent, Task> emit, CancellationToken ct)
    {
        var id = $"track_{Guid.NewGuid():N}";
        _sessions[id] = new SessionSummary(id, ownerUserId, SessionState.Running, DateTimeOffset.UtcNow,
            Name, placement.NodeId, request.Requirements);

        // Persist to DB so cascade validation and history queries work
        if (_dbFactory is not null)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            db.Sessions.Add(new SessionEntity
            {
                SessionId = id,
                OwnerUserId = ownerUserId,
                State = SessionState.Running,
                CreatedUtc = DateTimeOffset.UtcNow,
                Backend = Name,
                Node = placement.NodeId,
                RequirementsJson = System.Text.Json.JsonSerializer.Serialize(request.Requirements)
            });
            await db.SaveChangesAsync(ct);
        }

        return id;
    }

    public Task<bool> SendInputAsync(string sessionId, SendInputRequest request, CancellationToken ct) => Task.FromResult(true);

    public async Task StopAsync(string sessionId, bool forceKill, CancellationToken ct)
    {
        if (_sessions.TryGetValue(sessionId, out var s))
            _sessions[sessionId] = s with { State = SessionState.Stopped };

        if (_dbFactory is not null)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var entity = await db.Sessions.FindAsync(new object[] { sessionId }, ct);
            if (entity is not null)
            {
                entity.State = SessionState.Stopped;
                await db.SaveChangesAsync(ct);
            }
        }
    }

    public Task<SessionSummary?> GetAsync(string sessionId, CancellationToken ct)
        => Task.FromResult(_sessions.TryGetValue(sessionId, out var s) ? s : null);

    public Task<IReadOnlyList<SessionSummary>> ListAsync(string ownerUserId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<SessionSummary>>(
            _sessions.Values.Where(s => s.OwnerUserId == ownerUserId).ToArray());
}

file sealed class SpawnSimplePlacement : IPlacementEngine
{
    public PlacementDecision ChooseNode(string ownerUserId, SessionRequirements requirements, IReadOnlyList<NodeCapability> inventory)
        => new(inventory[0].Backend, inventory[0].NodeId);
}

file sealed class SpawnEmptySkillRegistry : ISkillRegistry
{
    public IReadOnlyList<SkillManifest> GetAll() => [];
    public SkillManifest? TryGet(string skillId) => null;
}

file sealed class SpawnAllowAllPolicy : ISkillPolicyService
{
    public PolicySnapshot GetPolicySnapshot() => new("allow-all", DateTimeOffset.UtcNow, [], [], []);
    public bool IsAllowed(string? skillId, SessionSummary session, bool elevated) => true;
}
