using AgentHub.Contracts;
using AgentHub.Orchestration;
using AgentHub.Orchestration.Backends;
using AgentHub.Orchestration.Data;
using AgentHub.Orchestration.Data.Entities;
using AgentHub.Orchestration.Worktree;
using AgentHub.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgentHub.Tests;

public class SshBackendWorktreeTests
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
                ["Ssh:GracePeriodSeconds"] = "1"
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
        var worktreeService = new WorktreeService(NullLogger<WorktreeService>.Instance);

        return new SshBackend(
            dbFactory,
            hostRegistry,
            factory,
            worktreeService,
            new NullServiceProvider(),
            NullLogger<SshBackend>.Instance,
            CreateConfig());
    }

    private static PlacementDecision CreatePlacement()
        => new("ssh", "ssh-host-01");

    [Fact]
    public async Task StartAsync_WithWorktreeId_CreatesWorktreeAndSetsFields()
    {
        var dbName = nameof(StartAsync_WithWorktreeId_CreatesWorktreeAndSetsFields);
        var mockConn = new MockSshHostConnection();
        // Order matters: follows SshBackend.StartAsync execution flow
        // 1. git rev-parse --show-toplevel (repo root detection)
        mockConn.EnqueueResponse("/home/user/repo");
        // 2. git worktree add (WorktreeService.CreateWorktreeAsync)
        mockConn.EnqueueResponse("Preparing worktree");
        // 3. start-session protocol command (HostCommandProtocol)
        mockConn.EnqueueSuccessResponse("start-session");

        var factory = new MockSshHostConnectionFactory();
        factory.EnqueueConnection(mockConn);

        var backend = CreateBackend(dbName, factory);
        var events = new List<SessionEvent>();

        var request = new StartSessionRequest(
            ImageOrProfile: "claude-code",
            Requirements: new SessionRequirements(ExecutionMode: ExecutionMode.Ssh, AcceptRisk: true),
            WorktreeId: "repo-123",
            Prompt: "Fix the login bug");

        var sessionId = await backend.StartAsync(
            "user-1", request, CreatePlacement(),
            e => { events.Add(e); return Task.CompletedTask; },
            CancellationToken.None);

        // Verify entity has worktree fields
        await using var db = CreateContext(dbName);
        var entity = await db.Sessions.FindAsync(sessionId);
        Assert.NotNull(entity);
        Assert.NotNull(entity.WorktreeBranch);
        Assert.StartsWith("agenthub/", entity.WorktreeBranch);
        Assert.Contains("fix-the-login-bug", entity.WorktreeBranch);
        Assert.NotNull(entity.WorktreePath);
        Assert.Contains(".worktrees/", entity.WorktreePath);
        Assert.False(entity.KeepBranch);

        // Verify SSH commands include git worktree add
        Assert.Contains(mockConn.CommandsSent, c => c.Contains("git worktree add"));
    }

    [Fact]
    public async Task StartAsync_WithoutWorktreeId_SkipsWorktreeCreation()
    {
        var dbName = nameof(StartAsync_WithoutWorktreeId_SkipsWorktreeCreation);
        var backend = CreateBackend(dbName);
        var events = new List<SessionEvent>();

        var request = new StartSessionRequest(
            ImageOrProfile: "claude-code",
            Requirements: new SessionRequirements(ExecutionMode: ExecutionMode.Ssh, AcceptRisk: true),
            Prompt: "Test prompt");

        var sessionId = await backend.StartAsync(
            "user-1", request, CreatePlacement(),
            e => { events.Add(e); return Task.CompletedTask; },
            CancellationToken.None);

        await using var db = CreateContext(dbName);
        var entity = await db.Sessions.FindAsync(sessionId);
        Assert.NotNull(entity);
        Assert.Null(entity.WorktreeBranch);
        Assert.StartsWith("/sessions/", entity.WorktreePath);
    }

    [Fact]
    public async Task StartAsync_WithKeepBranch_PersistsFlag()
    {
        var dbName = nameof(StartAsync_WithKeepBranch_PersistsFlag);
        var mockConn = new MockSshHostConnection();
        // 1. git rev-parse --show-toplevel
        mockConn.EnqueueResponse("/home/user/repo");
        // 2. git worktree add
        mockConn.EnqueueResponse("Preparing worktree");
        // 3. start-session protocol
        mockConn.EnqueueSuccessResponse("start-session");

        var factory = new MockSshHostConnectionFactory();
        factory.EnqueueConnection(mockConn);

        var backend = CreateBackend(dbName, factory);
        var events = new List<SessionEvent>();

        var request = new StartSessionRequest(
            ImageOrProfile: "claude-code",
            Requirements: new SessionRequirements(ExecutionMode: ExecutionMode.Ssh, AcceptRisk: true),
            WorktreeId: "repo-123",
            Prompt: "Feature work",
            KeepBranch: true);

        var sessionId = await backend.StartAsync(
            "user-1", request, CreatePlacement(),
            e => { events.Add(e); return Task.CompletedTask; },
            CancellationToken.None);

        await using var db = CreateContext(dbName);
        var entity = await db.Sessions.FindAsync(sessionId);
        Assert.NotNull(entity);
        Assert.True(entity.KeepBranch);
    }

    [Fact]
    public async Task StopAsync_WithWorktreeSession_CallsCleanup()
    {
        var dbName = nameof(StopAsync_WithWorktreeSession_CallsCleanup);
        var factory = new MockSshHostConnectionFactory();
        var backend = CreateBackend(dbName, factory);
        var events = new List<SessionEvent>();

        // Seed a worktree session directly in DB
        var sessionId = "ssh_wt_test";
        await using (var db = CreateContext(dbName))
        {
            db.Sessions.Add(new SessionEntity
            {
                SessionId = sessionId,
                OwnerUserId = "user-1",
                State = SessionState.Running,
                CreatedUtc = DateTimeOffset.UtcNow,
                Backend = "ssh",
                Node = "ssh-host-01",
                RequirementsJson = "{}",
                WorktreePath = "/home/user/repo/.worktrees/session1",
                WorktreeBranch = "agenthub/abc12345-test",
                KeepBranch = false,
                CleanupPolicy = "auto"
            });
            await db.SaveChangesAsync();
        }

        // Create a mock connection for this session
        var mockConn = new MockSshHostConnection();
        mockConn.EnqueueSuccessResponse("start-session"); // will be used by factory

        // We need to simulate that the session has a connection
        // Start a real session to register connection, then test stop
        var mockConn2 = new MockSshHostConnection();
        mockConn2.EnqueueSuccessResponse("start-session");
        factory.EnqueueConnection(mockConn2);

        var realSessionId = await backend.StartAsync(
            "user-1",
            new StartSessionRequest("claude-code",
                new SessionRequirements(ExecutionMode: ExecutionMode.Ssh, AcceptRisk: true),
                Prompt: "test"),
            CreatePlacement(),
            e => Task.CompletedTask,
            CancellationToken.None);

        // Enqueue responses for stop + worktree cleanup commands
        mockConn2.EnqueueSuccessResponse("stop-session", realSessionId);
        mockConn2.EnqueueSuccessResponse("force-kill", realSessionId);
        // Responses for git rev-parse, stash, worktree remove, branch -D
        mockConn2.EnqueueResponse("/home/user/repo");
        mockConn2.EnqueueResponse("No local changes");
        mockConn2.EnqueueResponse("");
        mockConn2.EnqueueResponse("Deleted branch");

        // Update the real session to have worktree fields
        await using (var db = CreateContext(dbName))
        {
            var entity = await db.Sessions.FindAsync(realSessionId);
            Assert.NotNull(entity);
            entity.WorktreeBranch = "agenthub/abc12345-test";
            entity.WorktreePath = "/home/user/repo/.worktrees/session1";
            entity.KeepBranch = false;
            entity.CleanupPolicy = "auto";
            await db.SaveChangesAsync();
        }

        await backend.StopAsync(realSessionId, forceKill: false, CancellationToken.None);

        // Should have sent git-related cleanup commands
        Assert.Contains(mockConn2.CommandsSent, c => c.Contains("git rev-parse") || c.Contains("git stash") || c.Contains("git worktree"));
    }

    [Fact]
    public async Task ForceKill_WithWorktreeSession_SkipsCleanupByDefault()
    {
        var dbName = nameof(ForceKill_WithWorktreeSession_SkipsCleanupByDefault);
        var factory = new MockSshHostConnectionFactory();
        var backend = CreateBackend(dbName, factory);

        // Start a session
        var mockConn = new MockSshHostConnection();
        mockConn.EnqueueSuccessResponse("start-session");
        factory.EnqueueConnection(mockConn);

        var sessionId = await backend.StartAsync(
            "user-1",
            new StartSessionRequest("claude-code",
                new SessionRequirements(ExecutionMode: ExecutionMode.Ssh, AcceptRisk: true),
                Prompt: "test"),
            CreatePlacement(),
            e => Task.CompletedTask,
            CancellationToken.None);

        // Let the background reader task complete (reads from empty mock stream)
        // before setting worktree fields, so its finally-block cleanup check
        // sees no worktree and exits early.
        await Task.Delay(200);

        // Set worktree fields with auto cleanup policy
        await using (var db = CreateContext(dbName))
        {
            var entity = await db.Sessions.FindAsync(sessionId);
            Assert.NotNull(entity);
            entity.WorktreeBranch = "agenthub/abc12345-test";
            entity.WorktreePath = "/home/user/repo/.worktrees/session1";
            entity.CleanupPolicy = "auto"; // "auto" on force-kill should skip
            await db.SaveChangesAsync();
        }

        // Record commands sent before force-kill to exclude background task commands
        var preForceKillCount = mockConn.CommandsSent.Count;

        // Enqueue force-kill response
        mockConn.EnqueueSuccessResponse("force-kill", sessionId);

        await backend.StopAsync(sessionId, forceKill: true, CancellationToken.None);

        // Force-kill with auto policy should NOT send git cleanup commands
        var forceKillCommands = mockConn.CommandsSent.Skip(preForceKillCount).ToList();
        Assert.DoesNotContain(forceKillCommands, c => c.Contains("git stash"));
        Assert.DoesNotContain(forceKillCommands, c => c.Contains("git worktree remove"));
    }

    [Fact]
    public async Task ForceKill_WithExplicitCleanupPolicy_RunsCleanup()
    {
        var dbName = nameof(ForceKill_WithExplicitCleanupPolicy_RunsCleanup);
        var factory = new MockSshHostConnectionFactory();
        var backend = CreateBackend(dbName, factory);

        var mockConn = new MockSshHostConnection();
        mockConn.EnqueueSuccessResponse("start-session");
        factory.EnqueueConnection(mockConn);

        var sessionId = await backend.StartAsync(
            "user-1",
            new StartSessionRequest("claude-code",
                new SessionRequirements(ExecutionMode: ExecutionMode.Ssh, AcceptRisk: true),
                Prompt: "test"),
            CreatePlacement(),
            e => Task.CompletedTask,
            CancellationToken.None);

        // Set worktree fields with explicit "cleanup" policy
        await using (var db = CreateContext(dbName))
        {
            var entity = await db.Sessions.FindAsync(sessionId);
            Assert.NotNull(entity);
            entity.WorktreeBranch = "agenthub/abc12345-test";
            entity.WorktreePath = "/home/user/repo/.worktrees/session1";
            entity.CleanupPolicy = "cleanup";
            await db.SaveChangesAsync();
        }

        // Enqueue responses for force-kill + git cleanup
        mockConn.EnqueueSuccessResponse("force-kill", sessionId);
        mockConn.EnqueueResponse("/home/user/repo"); // git rev-parse
        mockConn.EnqueueResponse("No local changes"); // stash
        mockConn.EnqueueResponse(""); // worktree remove
        mockConn.EnqueueResponse("Deleted branch"); // branch -D

        await backend.StopAsync(sessionId, forceKill: true, CancellationToken.None);

        // Should have sent git cleanup commands
        Assert.Contains(mockConn.CommandsSent, c => c.Contains("git rev-parse") || c.Contains("git stash"));
    }
}

// Helpers needed by this file (file-scoped, same pattern as SshBackendTests)
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
