using System.Text.Json;
using AgentHub.Contracts;
using AgentHub.Orchestration.Data.Entities;

namespace AgentHub.Orchestration.Data;

public static class EntityMappers
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // Session mappings

    public static SessionSummary ToDto(this SessionEntity entity)
    {
        var requirements = string.IsNullOrEmpty(entity.RequirementsJson)
            ? new SessionRequirements()
            : JsonSerializer.Deserialize<SessionRequirements>(entity.RequirementsJson, JsonOpts)
              ?? new SessionRequirements();

        return new SessionSummary(
            SessionId: entity.SessionId,
            OwnerUserId: entity.OwnerUserId,
            State: entity.State,
            CreatedUtc: entity.CreatedUtc,
            Backend: entity.Backend,
            Node: entity.Node,
            Requirements: requirements,
            WorktreePath: entity.WorktreePath,
            RiskAcceptedBy: entity.RiskAcceptedBy);
    }

    public static SessionEntity ToEntity(this SessionSummary dto, string? agentType = null)
    {
        return new SessionEntity
        {
            SessionId = dto.SessionId,
            OwnerUserId = dto.OwnerUserId,
            State = dto.State,
            CreatedUtc = dto.CreatedUtc,
            Backend = dto.Backend,
            Node = dto.Node,
            AgentType = agentType,
            RequirementsJson = JsonSerializer.Serialize(dto.Requirements, JsonOpts),
            WorktreePath = dto.WorktreePath,
            RiskAcceptedBy = dto.RiskAcceptedBy
        };
    }

    // SessionEvent mappings

    public static SessionEvent ToDto(this SessionEventEntity entity)
    {
        var meta = string.IsNullOrEmpty(entity.MetaJson)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, string>>(entity.MetaJson, JsonOpts);

        return new SessionEvent(
            SessionId: entity.SessionId,
            Kind: entity.Kind,
            TsUtc: entity.TsUtc,
            Data: entity.Data,
            Meta: meta);
    }

    public static SessionEventEntity ToEntity(this SessionEvent dto)
    {
        return new SessionEventEntity
        {
            SessionId = dto.SessionId,
            Kind = dto.Kind,
            TsUtc = dto.TsUtc,
            Data = dto.Data,
            MetaJson = dto.Meta is not null
                ? JsonSerializer.Serialize(dto.Meta, JsonOpts)
                : null
        };
    }

    // Host mappings

    public static HostRecord ToDto(this HostEntity entity)
    {
        var labels = string.IsNullOrEmpty(entity.LabelsJson)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, string>>(entity.LabelsJson, JsonOpts);

        var inventory = string.IsNullOrEmpty(entity.InventoryJson)
            ? null
            : JsonSerializer.Deserialize<HostInventory>(entity.InventoryJson, JsonOpts);

        return new HostRecord(
            HostId: entity.HostId,
            DisplayName: entity.DisplayName,
            Backend: entity.Backend,
            Os: entity.Os,
            Enabled: entity.Enabled,
            AllowSsh: entity.AllowSsh,
            Labels: labels,
            Address: entity.Address,
            CpuPercent: entity.CpuPercent,
            MemUsedMb: entity.MemUsedMb,
            MemTotalMb: entity.MemTotalMb,
            Inventory: inventory);
    }

    public static HostEntity ToEntity(this HostRecord dto)
    {
        return new HostEntity
        {
            HostId = dto.HostId,
            DisplayName = dto.DisplayName,
            Backend = dto.Backend,
            Os = dto.Os,
            Enabled = dto.Enabled,
            AllowSsh = dto.AllowSsh,
            LabelsJson = dto.Labels is not null
                ? JsonSerializer.Serialize(dto.Labels, JsonOpts)
                : null,
            Address = dto.Address,
            LastSeenUtc = DateTimeOffset.UtcNow,
            Status = "unknown",
            InventoryJson = dto.Inventory is not null
                ? JsonSerializer.Serialize(dto.Inventory, JsonOpts)
                : null
        };
    }
}
