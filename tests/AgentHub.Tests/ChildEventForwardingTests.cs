using System.Net.ServerSentEvents;
using System.Threading.Channels;
using AgentHub.Contracts;
using AgentHub.Orchestration.Data;
using AgentHub.Orchestration.Data.Entities;
using AgentHub.Orchestration.Events;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgentHub.Tests;

[Trait("Category", "Phase10")]
public class ChildEventForwardingTests
{
    private static IDbContextFactory<AgentHubDbContext> CreateFactory(string dbName)
        => new ForwardingTestDbContextFactory(dbName);

    private static async Task SeedParentChild(
        IDbContextFactory<AgentHubDbContext> dbFactory, string parentId, string childId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        db.Sessions.Add(new SessionEntity
        {
            SessionId = parentId,
            OwnerUserId = "user-1",
            State = SessionState.Running,
            CreatedUtc = DateTimeOffset.UtcNow,
            Backend = "test",
            Node = "node-01",
            RequirementsJson = "{}"
        });
        db.Sessions.Add(new SessionEntity
        {
            SessionId = childId,
            OwnerUserId = "user-1",
            State = SessionState.Running,
            CreatedUtc = DateTimeOffset.UtcNow,
            Backend = "test",
            Node = "node-01",
            ParentSessionId = parentId,
            RequirementsJson = "{}"
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ChildLifecycleEvent_ForwardedToParent()
    {
        var dbName = $"fwd-lifecycle-{Guid.NewGuid():N}";
        var dbFactory = CreateFactory(dbName);
        var subs = new SseSubscriptionManager();
        var service = new DurableEventService(dbFactory, subs);

        await SeedParentChild(dbFactory, "parent-1", "child-1");

        // Subscribe to parent stream
        var ch = Channel.CreateUnbounded<SseItem<SessionEvent>>();
        var connId = Guid.NewGuid();
        subs.AddSessionSubscriber("parent-1", connId, ch.Writer, includeChildren: false);

        // Emit a StateChanged event from child
        var childEvent = new SessionEvent("child-1", SessionEventKind.StateChanged,
            DateTimeOffset.UtcNow, "Running");
        await service.EmitAsync(childEvent);

        // Should receive forwarded event on parent channel
        Assert.True(ch.Reader.TryRead(out var parentItem));
        var forwarded = parentItem!.Data;
        Assert.Equal("parent-1", forwarded!.SessionId);
        Assert.Contains("[child-1]", forwarded.Data);
        Assert.Equal("true", forwarded.Meta!["forwarded"]);
        Assert.Equal("child-1", forwarded.Meta["sourceSessionId"]);
    }

    [Fact]
    public async Task ChildNonLifecycleEvent_NotForwardedWhenIncludeChildrenFalse()
    {
        var dbName = $"fwd-noninc-{Guid.NewGuid():N}";
        var dbFactory = CreateFactory(dbName);
        var subs = new SseSubscriptionManager();
        var service = new DurableEventService(dbFactory, subs);

        await SeedParentChild(dbFactory, "parent-2", "child-2");

        var ch = Channel.CreateUnbounded<SseItem<SessionEvent>>();
        subs.AddSessionSubscriber("parent-2", Guid.NewGuid(), ch.Writer, includeChildren: false);

        // Emit a StdOut event from child (non-lifecycle)
        await service.EmitAsync(new SessionEvent("child-2", SessionEventKind.StdOut,
            DateTimeOffset.UtcNow, "some output"));

        // Should NOT receive forwarded event (StdOut is not lifecycle)
        Assert.False(ch.Reader.TryRead(out _));
    }

    [Fact]
    public async Task ChildNonLifecycleEvent_ForwardedWhenIncludeChildrenTrue()
    {
        var dbName = $"fwd-inc-{Guid.NewGuid():N}";
        var dbFactory = CreateFactory(dbName);
        var subs = new SseSubscriptionManager();
        var service = new DurableEventService(dbFactory, subs);

        await SeedParentChild(dbFactory, "parent-3", "child-3");

        var ch = Channel.CreateUnbounded<SseItem<SessionEvent>>();
        subs.AddSessionSubscriber("parent-3", Guid.NewGuid(), ch.Writer, includeChildren: true);

        // Emit a StdOut event from child
        await service.EmitAsync(new SessionEvent("child-3", SessionEventKind.StdOut,
            DateTimeOffset.UtcNow, "some output"));

        Assert.True(ch.Reader.TryRead(out var item));
        Assert.Contains("[child-3]", item!.Data!.Data);
    }

    [Fact]
    public async Task ForwardedEvent_HasPrefixAndMeta()
    {
        var dbName = $"fwd-meta-{Guid.NewGuid():N}";
        var dbFactory = CreateFactory(dbName);
        var subs = new SseSubscriptionManager();
        var service = new DurableEventService(dbFactory, subs);

        await SeedParentChild(dbFactory, "parent-4", "child-4");

        var ch = Channel.CreateUnbounded<SseItem<SessionEvent>>();
        subs.AddSessionSubscriber("parent-4", Guid.NewGuid(), ch.Writer, includeChildren: false);

        await service.EmitAsync(new SessionEvent("child-4", SessionEventKind.ChildSpawned,
            DateTimeOffset.UtcNow, "Child session spawned",
            new Dictionary<string, string> { ["childSessionId"] = "grandchild-1" }));

        Assert.True(ch.Reader.TryRead(out var item));
        var ev = item!.Data!;
        Assert.Equal("parent-4", ev.SessionId);
        Assert.StartsWith("[child-4]", ev.Data);
        Assert.Equal("true", ev.Meta!["forwarded"]);
        Assert.Equal("child-4", ev.Meta["sourceSessionId"]);
    }

    [Fact]
    public async Task AlreadyForwardedEvent_NotReForwarded()
    {
        var dbName = $"fwd-loop-{Guid.NewGuid():N}";
        var dbFactory = CreateFactory(dbName);
        var subs = new SseSubscriptionManager();
        var service = new DurableEventService(dbFactory, subs);

        await SeedParentChild(dbFactory, "parent-5", "child-5");

        var ch = Channel.CreateUnbounded<SseItem<SessionEvent>>();
        subs.AddSessionSubscriber("parent-5", Guid.NewGuid(), ch.Writer, includeChildren: true);

        // Emit event already marked as forwarded
        await service.EmitAsync(new SessionEvent("child-5", SessionEventKind.StateChanged,
            DateTimeOffset.UtcNow, "[grandchild] Running",
            new Dictionary<string, string> { ["forwarded"] = "true" }));

        // Should NOT forward again (loop prevention)
        Assert.False(ch.Reader.TryRead(out _));
    }

    [Fact]
    public async Task ParentCacheInvalidation_Works()
    {
        var dbName = $"fwd-cache-{Guid.NewGuid():N}";
        var dbFactory = CreateFactory(dbName);
        var subs = new SseSubscriptionManager();
        var service = new DurableEventService(dbFactory, subs);

        await SeedParentChild(dbFactory, "parent-6", "child-6");

        // First emit should cache the parent lookup
        var ch = Channel.CreateUnbounded<SseItem<SessionEvent>>();
        subs.AddSessionSubscriber("parent-6", Guid.NewGuid(), ch.Writer, includeChildren: false);

        await service.EmitAsync(new SessionEvent("child-6", SessionEventKind.StateChanged,
            DateTimeOffset.UtcNow, "Running"));
        Assert.True(ch.Reader.TryRead(out _));

        // Invalidate cache
        service.InvalidateParentCache("child-6");

        // Update DB to remove parent relationship
        await using var db = await dbFactory.CreateDbContextAsync();
        var childEntity = await db.Sessions.FindAsync("child-6");
        childEntity!.ParentSessionId = null;
        await db.SaveChangesAsync();

        // Now emit again -- should not forward (parent relationship removed)
        await service.EmitAsync(new SessionEvent("child-6", SessionEventKind.StateChanged,
            DateTimeOffset.UtcNow, "Stopped"));
        Assert.False(ch.Reader.TryRead(out _));
    }
}

file sealed class ForwardingTestDbContextFactory(string dbName) : IDbContextFactory<AgentHubDbContext>
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
