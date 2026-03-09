using System.Text.Json;
using AgentHub.Contracts;
using AgentHub.Orchestration.Data;
using AgentHub.Orchestration.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgentHub.Tests;

public class InventoryMigrationTests
{
    private static AgentHubDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AgentHubDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AgentHubDbContext(options);
    }

    [Fact]
    public async Task SessionEntity_ParentSessionId_And_DispatchId_RoundTrip()
    {
        var dbName = nameof(SessionEntity_ParentSessionId_And_DispatchId_RoundTrip);
        await using var ctx = CreateContext(dbName);

        // Create parent session
        var parent = new SessionEntity
        {
            SessionId = "parent-001",
            OwnerUserId = "user-1",
            State = SessionState.Running,
            CreatedUtc = DateTimeOffset.UtcNow,
            Backend = "ssh",
            RequirementsJson = "{}"
        };
        ctx.Sessions.Add(parent);

        // Create child session with ParentSessionId and DispatchId
        var child = new SessionEntity
        {
            SessionId = "child-001",
            OwnerUserId = "user-1",
            State = SessionState.Pending,
            CreatedUtc = DateTimeOffset.UtcNow,
            Backend = "ssh",
            RequirementsJson = "{}",
            ParentSessionId = "parent-001",
            DispatchId = "dispatch-abc"
        };
        ctx.Sessions.Add(child);
        await ctx.SaveChangesAsync();

        var retrieved = await ctx.Sessions.FindAsync("child-001");
        Assert.NotNull(retrieved);
        Assert.Equal("parent-001", retrieved.ParentSessionId);
        Assert.Equal("dispatch-abc", retrieved.DispatchId);
    }

    [Fact]
    public async Task HostEntity_InventoryJson_RoundTrip()
    {
        var dbName = nameof(HostEntity_InventoryJson_RoundTrip);
        await using var ctx = CreateContext(dbName);

        var inventoryJson = """{"agents":[{"name":"claude","version":"1.0.0","path":"/usr/bin/claude","capabilities":["code-generation"]}],"diskFreeGb":100.5,"gitVersion":"2.40.0"}""";
        var host = new HostEntity
        {
            HostId = "host-inv-1",
            DisplayName = "Test Host",
            Backend = "ssh",
            Os = "linux",
            Enabled = true,
            AllowSsh = true,
            LastSeenUtc = DateTimeOffset.UtcNow,
            Status = "online",
            InventoryJson = inventoryJson
        };

        ctx.Hosts.Add(host);
        await ctx.SaveChangesAsync();

        var retrieved = await ctx.Hosts.FindAsync("host-inv-1");
        Assert.NotNull(retrieved);
        Assert.Equal(inventoryJson, retrieved.InventoryJson);
    }

    [Fact]
    public void ParentSessionId_FK_ConfiguredWithSetNull()
    {
        var dbName = nameof(ParentSessionId_FK_ConfiguredWithSetNull);
        using var ctx = CreateContext(dbName);

        var model = ctx.Model;
        var sessionEntityType = model.FindEntityType(typeof(SessionEntity))!;
        var fk = sessionEntityType.GetForeignKeys()
            .FirstOrDefault(f => f.Properties.Any(p => p.Name == "ParentSessionId"));

        Assert.NotNull(fk);
        Assert.Equal(DeleteBehavior.SetNull, fk.DeleteBehavior);
    }

    [Fact]
    public void EntityMappers_ToDto_MapsInventoryJson_NonNull()
    {
        var inventory = new HostInventory(
            [new AgentInfo("claude", "1.0.0", "/usr/bin/claude", ["code-generation"])],
            100.5,
            "2.40.0");
        var json = JsonSerializer.Serialize(inventory, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var entity = new HostEntity
        {
            HostId = "map-1",
            DisplayName = "Test",
            Backend = "ssh",
            Os = "linux",
            Enabled = true,
            AllowSsh = false,
            LastSeenUtc = DateTimeOffset.UtcNow,
            Status = "online",
            InventoryJson = json
        };

        var dto = entity.ToDto();

        Assert.NotNull(dto.Inventory);
        Assert.Single(dto.Inventory.Agents);
        Assert.Equal("claude", dto.Inventory.Agents[0].Name);
        Assert.Equal("1.0.0", dto.Inventory.Agents[0].Version);
        Assert.Equal(100.5, dto.Inventory.DiskFreeGb);
        Assert.Equal("2.40.0", dto.Inventory.GitVersion);
    }

    [Fact]
    public void EntityMappers_ToDto_MapsNullInventoryJson_Gracefully()
    {
        var entity = new HostEntity
        {
            HostId = "map-2",
            DisplayName = "Test",
            Backend = "ssh",
            Os = "linux",
            Enabled = true,
            AllowSsh = false,
            LastSeenUtc = DateTimeOffset.UtcNow,
            Status = "online",
            InventoryJson = null
        };

        var dto = entity.ToDto();

        Assert.Null(dto.Inventory);
    }
}
