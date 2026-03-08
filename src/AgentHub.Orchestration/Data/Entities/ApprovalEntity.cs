namespace AgentHub.Orchestration.Data.Entities;

/// <summary>
/// Persists an approval request for a destructive agent action.
/// </summary>
public class ApprovalEntity
{
    public string ApprovalId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Context { get; set; } = "{}";
    public string Status { get; set; } = "pending";
    public DateTimeOffset RequestedUtc { get; set; }
    public DateTimeOffset? ResolvedUtc { get; set; }
    public string? ResolvedBy { get; set; }
    public int? TimeoutSeconds { get; set; }
    public string? TimeoutAction { get; set; }

    public SessionEntity Session { get; set; } = null!;
}
