using System.Collections.Concurrent;

namespace AgentHub.Orchestration.Coordinator;

/// <summary>
/// Thread-safe in-memory tracker for per-host active session counts.
/// Registered as a singleton in DI.
/// </summary>
public sealed class ActiveSessionTracker
{
    private readonly ConcurrentDictionary<string, int> _counts = new();

    public void Increment(string hostId)
    {
        _counts.AddOrUpdate(hostId, 1, (_, current) => current + 1);
    }

    public void Decrement(string hostId)
    {
        _counts.AddOrUpdate(hostId, 0, (_, current) => Math.Max(0, current - 1));
    }

    public int GetCount(string hostId)
    {
        return _counts.TryGetValue(hostId, out var count) ? count : 0;
    }
}
