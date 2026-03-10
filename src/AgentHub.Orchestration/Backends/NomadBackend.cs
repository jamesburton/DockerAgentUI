using AgentHub.Contracts;

namespace AgentHub.Orchestration.Backends;

/// <summary>
/// Placeholder for a real Nomad adapter. Keep the in-memory backend as the runnable demo,
/// and replace this class when wiring HashiCorp Nomad jobs/allocation APIs.
/// </summary>
public sealed class NomadBackend : ISessionBackend
{
    public string Name => "nomad-real";

    public bool CanHandle(SessionRequirements requirements, NodeCapability node)
        => string.Equals(node.Backend, Name, StringComparison.OrdinalIgnoreCase);

    public Task<IReadOnlyList<NodeCapability>> GetInventoryAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<NodeCapability>>(Array.Empty<NodeCapability>());

    public Task<string> StartAsync(string ownerUserId, StartSessionRequest request, PlacementDecision placement, Func<SessionEvent, Task> emit, CancellationToken ct)
        => throw new NotImplementedException("Wire Nomad HTTP API here.");

    public Task<bool> SendInputAsync(string sessionId, SendInputRequest request, CancellationToken ct)
        => throw new NotImplementedException();

    public Task StopAsync(string sessionId, bool forceKill, CancellationToken ct)
        => throw new NotImplementedException();

    public Task<SessionSummary?> GetAsync(string sessionId, CancellationToken ct)
        => Task.FromResult<SessionSummary?>(null);

    public Task<IReadOnlyList<SessionSummary>> ListAsync(string ownerUserId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<SessionSummary>>(Array.Empty<SessionSummary>());
}
