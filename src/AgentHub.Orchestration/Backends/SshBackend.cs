using System.Collections.Concurrent;
using AgentHub.Contracts;

namespace AgentHub.Orchestration.Backends;

/// <summary>
/// Starter SSH backend. This is intentionally conservative:
/// - requires ExecutionMode.Ssh
/// - expects skill-based input
/// - does not attempt a live SSH connection in this scaffold
/// </summary>
public sealed class SshBackend : ISessionBackend
{
    public string Name => "ssh";
    private readonly ConcurrentDictionary<string, SessionSummary> _sessions = new();

    public bool CanHandle(SessionRequirements requirements, NodeCapability node)
        => requirements.ExecutionMode == ExecutionMode.Ssh
           && requirements.AcceptRisk
           && string.Equals(node.Backend, Name, StringComparison.OrdinalIgnoreCase)
           && node.AllowRiskyDirectExec;

    public Task<IReadOnlyList<NodeCapability>> GetInventoryAsync(CancellationToken ct)
    {
        var inv = new[]
        {
            new NodeCapability("ssh-win-dev-01", Name, "windows", 12, 32768, false, true, new Dictionary<string,string>{{"tier","dev"},{"acceptRisk","true"}}),
            new NodeCapability("ssh-linux-dev-01", Name, "linux", 8, 16384, false, true, new Dictionary<string,string>{{"tier","dev"},{"acceptRisk","true"}}),
        };
        return Task.FromResult<IReadOnlyList<NodeCapability>>(inv);
    }

    public async Task<string> StartAsync(string ownerUserId, StartSessionRequest request, PlacementDecision placement, Func<SessionEvent, Task> emit, CancellationToken ct)
    {
        var id = $"ssh_{Guid.NewGuid():N}";
        var worktreePath = request.WorktreeId is null
            ? $"C:/AgentSessions/{id}"
            : $"C:/AgentSessions/{request.WorktreeId}/{id}";

        var summary = new SessionSummary(id, ownerUserId, SessionState.Running, DateTimeOffset.UtcNow, Name, placement.NodeId, request.Requirements,
            WorktreePath: worktreePath,
            RiskAcceptedBy: ownerUserId);
        _sessions[id] = summary;

        await emit(new SessionEvent(id, SessionEventKind.StateChanged, DateTimeOffset.UtcNow, "Running"));
        await emit(new SessionEvent(id, SessionEventKind.Policy, DateTimeOffset.UtcNow, "SSH mode enabled with accept-risk flag.",
            new Dictionary<string, string> { ["backend"] = Name, ["host"] = placement.NodeId }));
        await emit(new SessionEvent(id, SessionEventKind.Info, DateTimeOffset.UtcNow, $"Provision remote working directory {worktreePath}."));
        await emit(new SessionEvent(id, SessionEventKind.Audit, DateTimeOffset.UtcNow, $"Would connect to {placement.NodeId} over SSH and start runner process."));

        return id;
    }

    public Task SendInputAsync(string sessionId, SendInputRequest request, CancellationToken ct)
        => Task.CompletedTask;

    public Task StopAsync(string sessionId, CancellationToken ct)
    {
        if (_sessions.TryGetValue(sessionId, out var s))
            _sessions[sessionId] = s with { State = SessionState.Stopped };
        return Task.CompletedTask;
    }

    public Task<SessionSummary?> GetAsync(string sessionId, CancellationToken ct)
        => Task.FromResult(_sessions.TryGetValue(sessionId, out var s) ? s : null);

    public Task<IReadOnlyList<SessionSummary>> ListAsync(string ownerUserId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<SessionSummary>>(_sessions.Values.Where(s => s.OwnerUserId == ownerUserId).OrderByDescending(s => s.CreatedUtc).ToArray());
}
