using System.Collections.Concurrent;
using AgentHub.Contracts;

namespace AgentHub.Orchestration.Backends;

public sealed class InMemoryBackend : ISessionBackend
{
    public string Name => "nomad";
    private readonly ConcurrentDictionary<string, SessionSummary> _sessions = new();

    public bool CanHandle(SessionRequirements requirements, NodeCapability node)
        => string.Equals(node.Backend, Name, StringComparison.OrdinalIgnoreCase);

    public Task<IReadOnlyList<NodeCapability>> GetInventoryAsync(CancellationToken ct)
    {
        var inv = new[]
        {
            new NodeCapability("nomad-dev-01", Name, "windows", 16, 32768, false, false, new Dictionary<string,string>{{"tier","dev"},{"region","local"}}),
            new NodeCapability("nomad-dev-02", Name, "linux", 8, 16384, false, false, new Dictionary<string,string>{{"tier","dev"},{"region","local"}}),
        };
        return Task.FromResult<IReadOnlyList<NodeCapability>>(inv);
    }

    public async Task<string> StartAsync(string ownerUserId, StartSessionRequest request, PlacementDecision placement, Func<SessionEvent, Task> emit, CancellationToken ct)
    {
        var id = $"sess_{Guid.NewGuid():N}";
        var summary = new SessionSummary(id, ownerUserId, SessionState.Running, DateTimeOffset.UtcNow, Name, placement.NodeId, request.Requirements,
            WorktreePath: request.WorktreeId is null ? null : $"/worktrees/{request.WorktreeId}");
        _sessions[id] = summary;

        await emit(new SessionEvent(id, SessionEventKind.StateChanged, DateTimeOffset.UtcNow, "Running"));
        await emit(new SessionEvent(id, SessionEventKind.Info, DateTimeOffset.UtcNow, $"Nomad-style session started for profile/image: {request.ImageOrProfile}"));

        _ = Task.Run(async () =>
        {
            for (var i = 0; i < 5; i++)
            {
                await Task.Delay(600, CancellationToken.None);
                await emit(new SessionEvent(id, SessionEventKind.StdOut, DateTimeOffset.UtcNow, $"nomad tick {i}",
                    new Dictionary<string, string> { ["node"] = placement.NodeId }));
            }
        });

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
