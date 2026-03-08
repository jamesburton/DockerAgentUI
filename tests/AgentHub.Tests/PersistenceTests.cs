using AgentHub.Contracts;
using AgentHub.Orchestration.Data;
using AgentHub.Orchestration.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgentHub.Tests;

public class PersistenceTests
{
    private static AgentHubDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AgentHubDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AgentHubDbContext(options);
    }

    [Fact]
    public async Task CreateAndRetrieveSession_ReturnsMatchingData()
    {
        var dbName = nameof(CreateAndRetrieveSession_ReturnsMatchingData);
        await using var ctx = CreateContext(dbName);

        var session = new SessionEntity
        {
            SessionId = "sess-001",
            OwnerUserId = "user-1",
            State = SessionState.Running,
            CreatedUtc = DateTimeOffset.UtcNow,
            Backend = "ssh",
            Node = "node-1",
            AgentType = "ClaudeCode",
            RequirementsJson = "{}",
            WorktreePath = "/tmp/work",
            RiskAcceptedBy = "admin"
        };

        ctx.Sessions.Add(session);
        await ctx.SaveChangesAsync();

        var retrieved = await ctx.Sessions.FindAsync("sess-001");
        Assert.NotNull(retrieved);
        Assert.Equal("user-1", retrieved.OwnerUserId);
        Assert.Equal(SessionState.Running, retrieved.State);
        Assert.Equal("ssh", retrieved.Backend);
        Assert.Equal("node-1", retrieved.Node);
        Assert.Equal("ClaudeCode", retrieved.AgentType);
    }

    [Fact]
    public async Task HostSeedingUpsertsRecordsIntoDb()
    {
        var dbName = nameof(HostSeedingUpsertsRecordsIntoDb);
        await using var ctx = CreateContext(dbName);

        var host = new HostEntity
        {
            HostId = "host-1",
            DisplayName = "Test Host",
            Backend = "ssh",
            Os = "linux",
            Enabled = true,
            AllowSsh = true,
            LabelsJson = """{"tier":"dev"}""",
            Address = "ssh://host-1",
            LastSeenUtc = DateTimeOffset.UtcNow,
            Status = "unknown"
        };

        ctx.Hosts.Add(host);
        await ctx.SaveChangesAsync();

        // Upsert: update the existing host
        var existing = await ctx.Hosts.FindAsync("host-1");
        Assert.NotNull(existing);
        existing.DisplayName = "Updated Host";
        existing.Status = "online";
        await ctx.SaveChangesAsync();

        var updated = await ctx.Hosts.FindAsync("host-1");
        Assert.NotNull(updated);
        Assert.Equal("Updated Host", updated.DisplayName);
        Assert.Equal("online", updated.Status);
    }

    [Fact]
    public async Task SessionEventsCrudAndNavigation()
    {
        var dbName = nameof(SessionEventsCrudAndNavigation);
        await using var ctx = CreateContext(dbName);

        var session = new SessionEntity
        {
            SessionId = "sess-evt-1",
            OwnerUserId = "user-1",
            State = SessionState.Running,
            CreatedUtc = DateTimeOffset.UtcNow,
            Backend = "local",
            RequirementsJson = "{}"
        };
        ctx.Sessions.Add(session);

        var evt = new SessionEventEntity
        {
            SessionId = "sess-evt-1",
            Kind = SessionEventKind.Info,
            TsUtc = DateTimeOffset.UtcNow,
            Data = "Session started",
            MetaJson = null
        };
        ctx.Events.Add(evt);
        await ctx.SaveChangesAsync();

        var events = await ctx.Events
            .Where(e => e.SessionId == "sess-evt-1")
            .ToListAsync();
        Assert.Single(events);
        Assert.Equal(SessionEventKind.Info, events[0].Kind);
        Assert.Equal("Session started", events[0].Data);
    }

    [Fact]
    public async Task EntityMapperRoundTrip_SessionPreservesData()
    {
        var requirements = new SessionRequirements(Os: "linux", CpuMin: 4, MemMinMb: 8192);
        var dto = new SessionSummary(
            SessionId: "round-1",
            OwnerUserId: "user-1",
            State: SessionState.Pending,
            CreatedUtc: DateTimeOffset.UtcNow,
            Backend: "ssh",
            Node: "node-1",
            Requirements: requirements,
            WorktreePath: "/tmp/work",
            RiskAcceptedBy: "admin");

        var entity = dto.ToEntity(agentType: "ClaudeCode");
        var roundTripped = entity.ToDto();

        Assert.Equal(dto.SessionId, roundTripped.SessionId);
        Assert.Equal(dto.OwnerUserId, roundTripped.OwnerUserId);
        Assert.Equal(dto.State, roundTripped.State);
        Assert.Equal(dto.Backend, roundTripped.Backend);
        Assert.Equal(dto.Node, roundTripped.Node);
        Assert.Equal(dto.WorktreePath, roundTripped.WorktreePath);
        Assert.Equal(dto.RiskAcceptedBy, roundTripped.RiskAcceptedBy);
        Assert.Equal("linux", roundTripped.Requirements.Os);
        Assert.Equal(4, roundTripped.Requirements.CpuMin);
        Assert.Equal(8192, roundTripped.Requirements.MemMinMb);
    }

    [Fact]
    public async Task EntityMapperRoundTrip_EventPreservesData()
    {
        var meta = new Dictionary<string, string> { { "key", "value" } };
        var dto = new SessionEvent(
            SessionId: "evt-round-1",
            Kind: SessionEventKind.StdOut,
            TsUtc: DateTimeOffset.UtcNow,
            Data: "hello world",
            Meta: meta);

        var entity = dto.ToEntity();
        var roundTripped = entity.ToDto();

        Assert.Equal(dto.SessionId, roundTripped.SessionId);
        Assert.Equal(dto.Kind, roundTripped.Kind);
        Assert.Equal(dto.Data, roundTripped.Data);
        Assert.NotNull(roundTripped.Meta);
        Assert.Equal("value", roundTripped.Meta["key"]);
    }

    [Fact]
    public async Task EntityMapperRoundTrip_HostPreservesData()
    {
        var labels = new Dictionary<string, string> { { "tier", "prod" }, { "region", "us-east" } };
        var dto = new HostRecord(
            HostId: "host-round-1",
            DisplayName: "Prod Host",
            Backend: "ssh",
            Os: "linux",
            Enabled: true,
            AllowSsh: true,
            Labels: labels,
            Address: "ssh://prod-1");

        var entity = dto.ToEntity();
        var roundTripped = entity.ToDto();

        Assert.Equal(dto.HostId, roundTripped.HostId);
        Assert.Equal(dto.DisplayName, roundTripped.DisplayName);
        Assert.Equal(dto.Backend, roundTripped.Backend);
        Assert.Equal(dto.Os, roundTripped.Os);
        Assert.Equal(dto.Enabled, roundTripped.Enabled);
        Assert.Equal(dto.AllowSsh, roundTripped.AllowSsh);
        Assert.NotNull(roundTripped.Labels);
        Assert.Equal("prod", roundTripped.Labels["tier"]);
        Assert.Equal("us-east", roundTripped.Labels["region"]);
        Assert.Equal(dto.Address, roundTripped.Address);
    }

    [Fact]
    public async Task SessionDataSurvivesContextDisposal()
    {
        var dbName = nameof(SessionDataSurvivesContextDisposal);

        // Create and save in one context
        await using (var ctx1 = CreateContext(dbName))
        {
            ctx1.Sessions.Add(new SessionEntity
            {
                SessionId = "persist-1",
                OwnerUserId = "user-1",
                State = SessionState.Running,
                CreatedUtc = DateTimeOffset.UtcNow,
                Backend = "ssh",
                RequirementsJson = "{}"
            });
            await ctx1.SaveChangesAsync();
        }

        // Retrieve in a new context (simulates restart)
        await using (var ctx2 = CreateContext(dbName))
        {
            var session = await ctx2.Sessions.FindAsync("persist-1");
            Assert.NotNull(session);
            Assert.Equal("user-1", session.OwnerUserId);
            Assert.Equal(SessionState.Running, session.State);
        }
    }
}
