using AgentHub.Contracts;
using AgentHub.Orchestration;
using AgentHub.Orchestration.Backends;
using AgentHub.Orchestration.Data;
using AgentHub.Orchestration.Data.Entities;
using AgentHub.Orchestration.HostDaemon;
using AgentHub.Orchestration.Monitoring;
using AgentHub.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgentHub.Tests;

public class SshBackendTests
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

    private static IConfiguration CreateConfig()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ssh:PrivateKeyPath"] = "/tmp/test_key",
                ["Ssh:Username"] = "test-user",
                ["Ssh:GracePeriodSeconds"] = "1" // Short grace for tests
            })
            .Build();

    private static SshBackend CreateBackend(
        string dbName,
        MockSshHostConnectionFactory? factory = null,
        IHostRegistry? hostRegistry = null)
    {
        var dbFactory = CreateFactory(dbName);
        factory ??= new MockSshHostConnectionFactory();
        hostRegistry ??= new TestHostRegistry();

        return new SshBackend(
            dbFactory,
            hostRegistry,
            factory,
            NullLogger<SshBackend>.Instance,
            CreateConfig());
    }

    private static StartSessionRequest CreateRequest(
        bool isFireAndForget = false,
        string? prompt = null)
    {
        return new StartSessionRequest(
            ImageOrProfile: "claude-code",
            Requirements: new SessionRequirements(
                ExecutionMode: ExecutionMode.Ssh,
                AcceptRisk: true),
            Prompt: prompt ?? "Test prompt",
            IsFireAndForget: isFireAndForget);
    }

    private static PlacementDecision CreatePlacement()
        => new("ssh", "ssh-host-01");

    // --- StartAsync Tests ---

    [Fact]
    public async Task StartAsync_PersistsSessionToDb_WithStateRunning()
    {
        var dbName = nameof(StartAsync_PersistsSessionToDb_WithStateRunning);
        var backend = CreateBackend(dbName);
        var events = new List<SessionEvent>();

        var sessionId = await backend.StartAsync(
            "user-1", CreateRequest(), CreatePlacement(),
            e => { events.Add(e); return Task.CompletedTask; },
            CancellationToken.None);

        Assert.NotNull(sessionId);
        Assert.StartsWith("ssh_", sessionId);

        // Verify session is in DB
        await using var db = CreateContext(dbName);
        var entity = await db.Sessions.FindAsync(sessionId);
        Assert.NotNull(entity);
        Assert.Equal(SessionState.Running, entity.State);
        Assert.Equal("user-1", entity.OwnerUserId);
        Assert.Equal("ssh", entity.Backend);
        Assert.Equal("ssh-host-01", entity.Node);
    }

    [Fact]
    public async Task StartAsync_WithIsFireAndForget_SetsEntityFlag()
    {
        var dbName = nameof(StartAsync_WithIsFireAndForget_SetsEntityFlag);
        var backend = CreateBackend(dbName);
        var events = new List<SessionEvent>();

        var sessionId = await backend.StartAsync(
            "user-1", CreateRequest(isFireAndForget: true), CreatePlacement(),
            e => { events.Add(e); return Task.CompletedTask; },
            CancellationToken.None);

        await using var db = CreateContext(dbName);
        var entity = await db.Sessions.FindAsync(sessionId);
        Assert.NotNull(entity);
        Assert.True(entity.IsFireAndForget);
    }

    [Fact]
    public async Task StartAsync_EmitsStateChangedEvent()
    {
        var dbName = nameof(StartAsync_EmitsStateChangedEvent);
        var backend = CreateBackend(dbName);
        var events = new List<SessionEvent>();

        await backend.StartAsync(
            "user-1", CreateRequest(), CreatePlacement(),
            e => { events.Add(e); return Task.CompletedTask; },
            CancellationToken.None);

        Assert.Contains(events, e => e.Kind == SessionEventKind.StateChanged && e.Data == "Running");
    }

    [Fact]
    public async Task StartAsync_PersistsPrompt()
    {
        var dbName = nameof(StartAsync_PersistsPrompt);
        var backend = CreateBackend(dbName);
        var events = new List<SessionEvent>();

        var sessionId = await backend.StartAsync(
            "user-1", CreateRequest(prompt: "Build the widget"), CreatePlacement(),
            e => { events.Add(e); return Task.CompletedTask; },
            CancellationToken.None);

        await using var db = CreateContext(dbName);
        var entity = await db.Sessions.FindAsync(sessionId);
        Assert.NotNull(entity);
        Assert.Equal("Build the widget", entity.Prompt);
    }

    // --- StopAsync Tests ---

    [Fact]
    public async Task StopAsync_ForceKill_SendsForceKillImmediately()
    {
        var dbName = nameof(StopAsync_ForceKill_SendsForceKillImmediately);
        var factory = new MockSshHostConnectionFactory();
        var backend = CreateBackend(dbName, factory);
        var events = new List<SessionEvent>();

        var sessionId = await backend.StartAsync(
            "user-1", CreateRequest(), CreatePlacement(),
            e => { events.Add(e); return Task.CompletedTask; },
            CancellationToken.None);

        var mock = factory.LastCreated!;
        // Enqueue response for force-kill command
        mock.EnqueueSuccessResponse("force-kill", sessionId);

        await backend.StopAsync(sessionId, forceKill: true, CancellationToken.None);

        // Should have sent force-kill command (after the start-session command)
        Assert.True(mock.CommandsSent.Count >= 2);
        var lastCommand = mock.CommandsSent.Last();
        Assert.Contains("force-kill", lastCommand);

        // Session should be stopped in DB
        await using var db = CreateContext(dbName);
        var entity = await db.Sessions.FindAsync(sessionId);
        Assert.NotNull(entity);
        Assert.Equal(SessionState.Stopped, entity.State);
    }

    [Fact]
    public async Task StopAsync_Graceful_SendsStopThenForceKillAfterTimeout()
    {
        var dbName = nameof(StopAsync_Graceful_SendsStopThenForceKillAfterTimeout);
        var factory = new MockSshHostConnectionFactory();
        var backend = CreateBackend(dbName, factory);
        var events = new List<SessionEvent>();

        var sessionId = await backend.StartAsync(
            "user-1", CreateRequest(), CreatePlacement(),
            e => { events.Add(e); return Task.CompletedTask; },
            CancellationToken.None);

        var mock = factory.LastCreated!;
        // Enqueue responses for stop-session and force-kill commands
        mock.EnqueueSuccessResponse("stop-session", sessionId);
        mock.EnqueueSuccessResponse("force-kill", sessionId);

        await backend.StopAsync(sessionId, forceKill: false, CancellationToken.None);

        // Should have sent stop-session first (command at index 1, after start-session)
        var commands = mock.CommandsSent;
        Assert.True(commands.Count >= 2);
        Assert.Contains(commands, c => c.Contains("stop-session"));

        // Session should be stopped in DB
        await using var db = CreateContext(dbName);
        var entity = await db.Sessions.FindAsync(sessionId);
        Assert.NotNull(entity);
        Assert.Equal(SessionState.Stopped, entity.State);
        Assert.NotNull(entity.CompletedUtc);
    }

    // --- ListAsync Tests ---

    [Fact]
    public async Task ListAsync_ReturnsSessionsFromDb()
    {
        var dbName = nameof(ListAsync_ReturnsSessionsFromDb);
        var backend = CreateBackend(dbName);

        // Seed sessions directly in DB
        await using (var db = CreateContext(dbName))
        {
            db.Sessions.Add(new SessionEntity
            {
                SessionId = "ssh_001", OwnerUserId = "user-1", State = SessionState.Running,
                CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-2), Backend = "ssh", RequirementsJson = "{}"
            });
            db.Sessions.Add(new SessionEntity
            {
                SessionId = "ssh_002", OwnerUserId = "user-1", State = SessionState.Stopped,
                CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-1), Backend = "ssh", RequirementsJson = "{}"
            });
            db.Sessions.Add(new SessionEntity
            {
                SessionId = "ssh_003", OwnerUserId = "user-2", State = SessionState.Running,
                CreatedUtc = DateTimeOffset.UtcNow, Backend = "ssh", RequirementsJson = "{}"
            });
            await db.SaveChangesAsync();
        }

        var sessions = await backend.ListAsync("user-1", CancellationToken.None);

        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, s => Assert.Equal("user-1", s.OwnerUserId));
        // Should be ordered by CreatedUtc descending
        Assert.Equal("ssh_002", sessions[0].SessionId);
        Assert.Equal("ssh_001", sessions[1].SessionId);
    }

    // --- GetAsync Tests ---

    [Fact]
    public async Task GetAsync_ReturnsNull_ForNonexistentSession()
    {
        var dbName = nameof(GetAsync_ReturnsNull_ForNonexistentSession);
        var backend = CreateBackend(dbName);

        var result = await backend.GetAsync("nonexistent", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_ReturnsMappedSessionSummary_ForExistingSession()
    {
        var dbName = nameof(GetAsync_ReturnsMappedSessionSummary_ForExistingSession);
        var backend = CreateBackend(dbName);

        await using (var db = CreateContext(dbName))
        {
            db.Sessions.Add(new SessionEntity
            {
                SessionId = "ssh_existing", OwnerUserId = "user-1", State = SessionState.Running,
                CreatedUtc = DateTimeOffset.UtcNow, Backend = "ssh", Node = "node-1",
                RequirementsJson = "{}", WorktreePath = "/sessions/test"
            });
            await db.SaveChangesAsync();
        }

        var result = await backend.GetAsync("ssh_existing", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("ssh_existing", result.SessionId);
        Assert.Equal("user-1", result.OwnerUserId);
        Assert.Equal(SessionState.Running, result.State);
        Assert.Equal("ssh", result.Backend);
        Assert.Equal("node-1", result.Node);
    }

    // --- SessionMonitorService Tests ---

    [Fact]
    public async Task SessionMonitor_MarksOrphanedSessionAsFailed()
    {
        var dbName = nameof(SessionMonitor_MarksOrphanedSessionAsFailed);
        var dbFactory = CreateFactory(dbName);

        // Seed a running session with old event timestamp (> 90s ago)
        await using (var db = dbFactory.CreateDbContext())
        {
            var session = new SessionEntity
            {
                SessionId = "orphan-1",
                OwnerUserId = "user-1",
                State = SessionState.Running,
                CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                Backend = "ssh",
                RequirementsJson = "{}"
            };
            db.Sessions.Add(session);

            // Last event was 2 minutes ago (exceeds 90s timeout)
            db.Events.Add(new SessionEventEntity
            {
                SessionId = "orphan-1",
                Kind = SessionEventKind.Heartbeat,
                TsUtc = DateTimeOffset.UtcNow.AddSeconds(-120),
                Data = "heartbeat"
            });
            await db.SaveChangesAsync();
        }

        var monitor = new SessionMonitorService(
            dbFactory,
            NullLogger<SessionMonitorService>.Instance,
            checkInterval: TimeSpan.FromMilliseconds(100),
            heartbeatTimeout: TimeSpan.FromSeconds(90));

        await monitor.RunOnceAsync(CancellationToken.None);

        // Session should be marked as Failed
        await using var checkDb = dbFactory.CreateDbContext();
        var entity = await checkDb.Sessions.FindAsync("orphan-1");
        Assert.NotNull(entity);
        Assert.Equal(SessionState.Failed, entity.State);
    }

    [Fact]
    public async Task SessionMonitor_DoesNotMarkHealthySessions()
    {
        var dbName = nameof(SessionMonitor_DoesNotMarkHealthySessions);
        var dbFactory = CreateFactory(dbName);

        // Seed a running session with recent event
        await using (var db = dbFactory.CreateDbContext())
        {
            var session = new SessionEntity
            {
                SessionId = "healthy-1",
                OwnerUserId = "user-1",
                State = SessionState.Running,
                CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                Backend = "ssh",
                RequirementsJson = "{}"
            };
            db.Sessions.Add(session);

            // Last event was 10 seconds ago (within 90s timeout)
            db.Events.Add(new SessionEventEntity
            {
                SessionId = "healthy-1",
                Kind = SessionEventKind.Heartbeat,
                TsUtc = DateTimeOffset.UtcNow.AddSeconds(-10),
                Data = "heartbeat"
            });
            await db.SaveChangesAsync();
        }

        var monitor = new SessionMonitorService(
            dbFactory,
            NullLogger<SessionMonitorService>.Instance,
            checkInterval: TimeSpan.FromMilliseconds(100),
            heartbeatTimeout: TimeSpan.FromSeconds(90));

        await monitor.RunOnceAsync(CancellationToken.None);

        // Session should still be Running
        await using var checkDb = dbFactory.CreateDbContext();
        var entity = await checkDb.Sessions.FindAsync("healthy-1");
        Assert.NotNull(entity);
        Assert.Equal(SessionState.Running, entity.State);
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

file sealed class TestHostRegistry : IHostRegistry
{
    public Task<IReadOnlyList<HostRecord>> ListAsync(CancellationToken ct)
    {
        var hosts = new List<HostRecord>
        {
            new("ssh-host-01", "Test SSH Host", "ssh", "linux", true, true,
                new Dictionary<string, string> { ["tier"] = "dev" }, "ssh://test-host")
        };
        return Task.FromResult<IReadOnlyList<HostRecord>>(hosts);
    }

    public Task<HostRecord?> GetAsync(string hostId, CancellationToken ct)
    {
        if (hostId == "ssh-host-01")
            return Task.FromResult<HostRecord?>(
                new HostRecord("ssh-host-01", "Test SSH Host", "ssh", "linux", true, true,
                    new Dictionary<string, string> { ["tier"] = "dev" }, "ssh://test-host"));
        return Task.FromResult<HostRecord?>(null);
    }
}
