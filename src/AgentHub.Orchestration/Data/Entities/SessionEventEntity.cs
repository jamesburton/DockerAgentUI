using AgentHub.Contracts;

namespace AgentHub.Orchestration.Data.Entities;

public class SessionEventEntity
{
    public long Id { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public SessionEventKind Kind { get; set; }
    public DateTimeOffset TsUtc { get; set; }
    public string Data { get; set; } = string.Empty;
    public string? MetaJson { get; set; }

    public SessionEntity Session { get; set; } = null!;
}
