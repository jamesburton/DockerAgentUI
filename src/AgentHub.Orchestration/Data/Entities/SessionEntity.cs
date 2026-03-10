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

    // Phase 2 additions
    public DateTimeOffset? CompletedUtc { get; set; }
    public int? ExitCode { get; set; }
    public string? CleanupState { get; set; }
    public bool IsFireAndForget { get; set; }
    public string? Prompt { get; set; }
    public string? TimeLimit { get; set; }
    public string? CleanupPolicy { get; set; }

    // Phase 7 additions
    public string? ParentSessionId { get; set; }
    public string? DispatchId { get; set; }

    // Phase 9 additions
    public string? WorktreeBranch { get; set; }
    public bool KeepBranch { get; set; }
    public string? RepoPath { get; set; }

    public List<SessionEventEntity> Events { get; set; } = [];
    public List<ApprovalEntity> Approvals { get; set; } = [];
}
