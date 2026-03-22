using AgentHub.Contracts;
using AgentHub.Orchestration.Coordinator;
using AgentHub.Orchestration.Data;
using AgentHub.Orchestration.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace AgentHub.Tests;

[Trait("Category", "Phase10")]
public class CascadeLimitTests
{
    private static AgentHubDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<AgentHubDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AgentHubDbContext(options);
    }

    private static IOptions<CoordinationOptions> DefaultOptions() =>
        Options.Create(new CoordinationOptions { MaxDepth = 3, MaxChildrenPerParent = 10 });

    private static SessionEntity MakeSession(string id, string? parentId = null, SessionState state = SessionState.Running)
    {
        return new SessionEntity
        {
            SessionId = id,
            OwnerUserId = "user1",
            State = state,
            CreatedUtc = DateTimeOffset.UtcNow,
            Backend = "ssh",
            Node = "host1",
            ParentSessionId = parentId,
            RequirementsJson = "{}"
        };
    }

    [Fact]
    public async Task DepthZeroParent_AllowsSpawn()
    {
        using var db = CreateInMemoryDb();
        var parent = MakeSession("parent-1");
        db.Sessions.Add(parent);
        await db.SaveChangesAsync();

        var opts = DefaultOptions();

        // Should not throw
        await SessionCoordinator.ValidateCascadeLimitsAsync(db, "parent-1", opts.Value);
    }

    [Fact]
    public async Task DepthAtMaxDepth_Rejects()
    {
        using var db = CreateInMemoryDb();
        // Create chain: root -> child -> grandchild -> great-grandchild (depth 3)
        db.Sessions.Add(MakeSession("root"));
        db.Sessions.Add(MakeSession("child", parentId: "root"));
        db.Sessions.Add(MakeSession("grandchild", parentId: "child"));
        db.Sessions.Add(MakeSession("great-grandchild", parentId: "grandchild"));
        await db.SaveChangesAsync();

        var opts = DefaultOptions(); // MaxDepth = 3

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SessionCoordinator.ValidateCascadeLimitsAsync(db, "great-grandchild", opts.Value));
        Assert.Contains("depth", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MaxChildrenPerParent_Rejects()
    {
        using var db = CreateInMemoryDb();
        var parent = MakeSession("parent-1");
        db.Sessions.Add(parent);

        var opts = Options.Create(new CoordinationOptions { MaxDepth = 3, MaxChildrenPerParent = 3 });

        // Add 3 running children
        for (int i = 0; i < 3; i++)
            db.Sessions.Add(MakeSession($"child-{i}", parentId: "parent-1"));
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SessionCoordinator.ValidateCascadeLimitsAsync(db, "parent-1", opts.Value));
        Assert.Contains("children", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FewerThanMaxChildren_AllowsSpawn()
    {
        using var db = CreateInMemoryDb();
        var parent = MakeSession("parent-1");
        db.Sessions.Add(parent);

        var opts = Options.Create(new CoordinationOptions { MaxDepth = 3, MaxChildrenPerParent = 5 });

        // Add 2 running children (below max of 5)
        db.Sessions.Add(MakeSession("child-0", parentId: "parent-1"));
        db.Sessions.Add(MakeSession("child-1", parentId: "parent-1"));
        // One stopped child should not count
        db.Sessions.Add(MakeSession("child-2", parentId: "parent-1", state: SessionState.Stopped));
        await db.SaveChangesAsync();

        // Should not throw
        await SessionCoordinator.ValidateCascadeLimitsAsync(db, "parent-1", opts.Value);
    }
}
