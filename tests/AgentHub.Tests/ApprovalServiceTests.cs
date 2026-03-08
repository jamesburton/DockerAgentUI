using System.Text.Json;
using AgentHub.Contracts;
using AgentHub.Orchestration.Coordinator;
using AgentHub.Orchestration.Data;
using AgentHub.Orchestration.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgentHub.Tests;

public class ApprovalServiceTests : IDisposable
{
    private readonly DbContextOptions<AgentHubDbContext> _dbOptions;
    private readonly ApprovalService _sut;

    public ApprovalServiceTests()
    {
        _dbOptions = new DbContextOptionsBuilder<AgentHubDbContext>()
            .UseInMemoryDatabase($"ApprovalTests_{Guid.NewGuid():N}")
            .Options;

        // Seed a session so FK constraints don't fail
        using var ctx = new AgentHubDbContext(_dbOptions);
        ctx.Database.EnsureCreated();
        ctx.Sessions.Add(new SessionEntity
        {
            SessionId = "sess-1",
            OwnerUserId = "user-1",
            Backend = "ssh",
            CreatedUtc = DateTimeOffset.UtcNow
        });
        ctx.SaveChanges();

        var factory = new TestDbContextFactory(_dbOptions);
        _sut = new ApprovalService(factory, NullLogger<ApprovalService>.Instance);
    }

    public void Dispose()
    {
        using var ctx = new AgentHubDbContext(_dbOptions);
        ctx.Database.EnsureDeleted();
    }

    [Fact]
    public async Task RequestApprovalAsync_EmitsApprovalRequestEvent()
    {
        var events = new List<SessionEvent>();
        var context = MakeContext();

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            // Find and resolve the pending approval
            var pending = _sut.GetPendingApprovalIds();
            foreach (var id in pending)
                _sut.ResolveApproval(id, true, "tester");
        });

        await _sut.RequestApprovalAsync("sess-1", context, e => { events.Add(e); return Task.CompletedTask; }, CancellationToken.None);

        Assert.Single(events);
        Assert.Equal(SessionEventKind.ApprovalRequest, events[0].Kind);
        Assert.NotNull(events[0].Meta);
        Assert.True(events[0].Meta!.ContainsKey("approvalId"));
    }

    [Fact]
    public async Task RequestApprovalAsync_PersistsApprovalEntity()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            var pending = _sut.GetPendingApprovalIds();
            foreach (var id in pending)
                _sut.ResolveApproval(id, true, "tester");
        });

        await _sut.RequestApprovalAsync("sess-1", MakeContext(), NoOpEmit, CancellationToken.None);

        using var ctx = new AgentHubDbContext(_dbOptions);
        var entity = await ctx.Approvals.FirstOrDefaultAsync();
        Assert.NotNull(entity);
        Assert.Equal("sess-1", entity.SessionId);
    }

    [Fact]
    public async Task RequestApprovalAsync_BlocksUntilResolved()
    {
        var resolved = false;
        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            resolved = true;
            var pending = _sut.GetPendingApprovalIds();
            foreach (var id in pending)
                _sut.ResolveApproval(id, true, "tester");
        });

        await _sut.RequestApprovalAsync("sess-1", MakeContext(), NoOpEmit, CancellationToken.None);
        Assert.True(resolved, "Should have blocked until ResolveApproval was called");
    }

    [Fact]
    public async Task ResolveApproval_Approved_ReturnsApprovedDecision()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            var pending = _sut.GetPendingApprovalIds();
            foreach (var id in pending)
                _sut.ResolveApproval(id, true, "tester");
        });

        var result = await _sut.RequestApprovalAsync("sess-1", MakeContext(), NoOpEmit, CancellationToken.None);
        Assert.Equal(ApprovalDecision.Approved, result);

        // Allow fire-and-forget DB update to complete
        await Task.Delay(200);
        using var ctx = new AgentHubDbContext(_dbOptions);
        var entity = await ctx.Approvals.FirstAsync();
        Assert.Equal("approved", entity.Status);
    }

    [Fact]
    public async Task ResolveApproval_Denied_ReturnsDeniedDecision()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            var pending = _sut.GetPendingApprovalIds();
            foreach (var id in pending)
                _sut.ResolveApproval(id, false, "tester");
        });

        var result = await _sut.RequestApprovalAsync("sess-1", MakeContext(), NoOpEmit, CancellationToken.None);
        Assert.Equal(ApprovalDecision.Denied, result);

        // Allow fire-and-forget DB update to complete
        await Task.Delay(200);
        using var ctx = new AgentHubDbContext(_dbOptions);
        var entity = await ctx.Approvals.FirstAsync();
        Assert.Equal("denied", entity.Status);
    }

    [Fact]
    public async Task RequestApprovalAsync_Timeout_FiresTimeoutAction_Stop()
    {
        var context = MakeContext(timeoutSeconds: 1, timeoutAction: "stop");

        var result = await _sut.RequestApprovalAsync("sess-1", context, NoOpEmit, CancellationToken.None);

        Assert.Equal(ApprovalDecision.Denied, result);

        using var ctx = new AgentHubDbContext(_dbOptions);
        var entity = await ctx.Approvals.FirstAsync();
        Assert.Equal("timed-out", entity.Status);
    }

    [Fact]
    public async Task RequestApprovalAsync_Timeout_AutoApprove()
    {
        var context = MakeContext(timeoutSeconds: 1, timeoutAction: "auto-approve");

        var result = await _sut.RequestApprovalAsync("sess-1", context, NoOpEmit, CancellationToken.None);

        Assert.Equal(ApprovalDecision.Approved, result);
    }

    [Fact]
    public async Task RequestApprovalAsync_Timeout_ContinueDefault()
    {
        var context = MakeContext(timeoutSeconds: 1, timeoutAction: "continue");

        var result = await _sut.RequestApprovalAsync("sess-1", context, NoOpEmit, CancellationToken.None);

        Assert.Equal(ApprovalDecision.Approved, result);
    }

    [Fact]
    public async Task RequestApprovalAsync_SkipPermissions_AutoApproves()
    {
        var events = new List<SessionEvent>();
        var context = MakeContext(skipPermissions: true);

        var result = await _sut.RequestApprovalAsync("sess-1", context, e => { events.Add(e); return Task.CompletedTask; }, CancellationToken.None);

        Assert.Equal(ApprovalDecision.AutoApproved, result);
        Assert.Empty(events); // No SSE event emitted
    }

    [Fact]
    public async Task RaceCondition_ResolveAndTimeout_FirstOneWins()
    {
        // Short timeout with simultaneous resolve
        var context = MakeContext(timeoutSeconds: 1, timeoutAction: "stop");

        // Start approval, then resolve very quickly before timeout
        _ = Task.Run(async () =>
        {
            await Task.Delay(20);
            var pending = _sut.GetPendingApprovalIds();
            foreach (var id in pending)
                _sut.ResolveApproval(id, true, "fast-user");
        });

        var result = await _sut.RequestApprovalAsync("sess-1", context, NoOpEmit, CancellationToken.None);

        // First one wins: should be Approved because resolve fires before timeout
        Assert.Equal(ApprovalDecision.Approved, result);
    }

    [Fact]
    public async Task ApprovalContext_IncludesFullDetails()
    {
        var events = new List<SessionEvent>();
        var context = new ApprovalContext(
            Action: "Bash",
            Command: "rm -rf /tmp/important",
            FilePath: "/tmp/important",
            DiffPreview: "- old content\n+ new content",
            TimeoutSeconds: 1,
            TimeoutAction: "stop",
            SkipPermissionPrompts: false);

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            var pending = _sut.GetPendingApprovalIds();
            foreach (var id in pending)
                _sut.ResolveApproval(id, true, "tester");
        });

        await _sut.RequestApprovalAsync("sess-1", context, e => { events.Add(e); return Task.CompletedTask; }, CancellationToken.None);

        var eventData = JsonSerializer.Deserialize<JsonElement>(events[0].Data);
        Assert.Equal("Bash", eventData.GetProperty("action").GetString());
        Assert.Equal("rm -rf /tmp/important", eventData.GetProperty("command").GetString());
        Assert.Equal("/tmp/important", eventData.GetProperty("filePath").GetString());
        Assert.Equal("- old content\n+ new content", eventData.GetProperty("diffPreview").GetString());
    }

    // Helper: no-op emit function
    private static Task NoOpEmit(SessionEvent e) => Task.CompletedTask;

    private static ApprovalContext MakeContext(
        int? timeoutSeconds = null,
        string? timeoutAction = null,
        bool skipPermissions = false) =>
        new(
            Action: "Write",
            Command: "echo test",
            FilePath: "/tmp/test.txt",
            DiffPreview: null,
            TimeoutSeconds: timeoutSeconds,
            TimeoutAction: timeoutAction,
            SkipPermissionPrompts: skipPermissions);
}

/// <summary>
/// Simple IDbContextFactory implementation for tests using InMemory provider.
/// </summary>
internal sealed class TestDbContextFactory(DbContextOptions<AgentHubDbContext> options)
    : IDbContextFactory<AgentHubDbContext>
{
    public AgentHubDbContext CreateDbContext() => new(options);
}
