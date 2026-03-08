using AgentHub.Contracts;

namespace AgentHub.Orchestration.Data.Entities;

public class SessionEntity
{
    public string SessionId { get; set; } = string.Empty;
    public string OwnerUserId { get; set; } = string.Empty;
    public SessionState State { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public string Backend { get; set; } = string.Empty;
    public string? Node { get; set; }
    public string? AgentType { get; set; }
    public string RequirementsJson { get; set; } = "{}";
    public string? WorktreePath { get; set; }
    public string? RiskAcceptedBy { get; set; }

    public List<SessionEventEntity> Events { get; set; } = [];
}
