using System.Collections.Concurrent;
using System.Net.ServerSentEvents;
using System.Threading.Channels;
using AgentHub.Contracts;

namespace AgentHub.Orchestration.Events;

/// <summary>
/// Manages SSE subscriber lifecycle with cleanup.
/// Uses ConcurrentDictionary keyed by connection ID (not ConcurrentBag) to support reliable removal.
/// </summary>
public sealed class SseSubscriptionManager
{
    /// <summary>
    /// Wraps a channel writer with subscriber options.
    /// </summary>
    private sealed record SubscriberInfo(ChannelWriter<SseItem<SessionEvent>> Writer, bool IncludeChildren);

    // Per-session subscribers: sessionId -> { connectionId -> SubscriberInfo }
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, SubscriberInfo>> _sessionSubs = new();

    // Fleet-wide subscribers: connectionId -> ChannelWriter
    private readonly ConcurrentDictionary<Guid, ChannelWriter<SseItem<SessionEvent>>> _fleetSubs = new();

    public void AddSessionSubscriber(string sessionId, Guid connectionId, ChannelWriter<SseItem<SessionEvent>> writer, bool includeChildren = false)
    {
        var subs = _sessionSubs.GetOrAdd(sessionId, _ => new ConcurrentDictionary<Guid, SubscriberInfo>());
        subs[connectionId] = new SubscriberInfo(writer, includeChildren);
    }

    public void RemoveSessionSubscriber(string sessionId, Guid connectionId)
    {
        if (_sessionSubs.TryGetValue(sessionId, out var subs))
        {
            subs.TryRemove(connectionId, out _);
            // Clean up empty session dictionaries to prevent unbounded growth
            if (subs.IsEmpty)
                _sessionSubs.TryRemove(sessionId, out _);
        }
    }

    public void AddFleetSubscriber(Guid connectionId, ChannelWriter<SseItem<SessionEvent>> writer)
    {
        _fleetSubs[connectionId] = writer;
    }

    public void RemoveFleetSubscriber(Guid connectionId)
    {
        _fleetSubs.TryRemove(connectionId, out _);
    }

    public void BroadcastToSession(string sessionId, SseItem<SessionEvent> sseItem)
    {
        if (_sessionSubs.TryGetValue(sessionId, out var subs))
        {
            foreach (var kvp in subs)
            {
                // If writer is completed/faulted, TryWrite returns false -- clean up
                if (!kvp.Value.Writer.TryWrite(sseItem))
                {
                    subs.TryRemove(kvp.Key, out _);
                }
            }
        }
    }

    /// <summary>
    /// Broadcasts a forwarded child event to parent session subscribers.
    /// Lifecycle events are always forwarded; non-lifecycle events only if subscriber opted in.
    /// </summary>
    public void BroadcastToParent(string parentSessionId, SseItem<SessionEvent> sseItem, bool isLifecycleEvent)
    {
        if (_sessionSubs.TryGetValue(parentSessionId, out var subs))
        {
            foreach (var kvp in subs)
            {
                if (isLifecycleEvent || kvp.Value.IncludeChildren)
                {
                    if (!kvp.Value.Writer.TryWrite(sseItem))
                    {
                        subs.TryRemove(kvp.Key, out _);
                    }
                }
            }
        }
    }

    public void BroadcastToFleet(SseItem<SessionEvent> sseItem)
    {
        foreach (var kvp in _fleetSubs)
        {
            if (!kvp.Value.TryWrite(sseItem))
            {
                _fleetSubs.TryRemove(kvp.Key, out _);
            }
        }
    }

    /// <summary>
    /// Returns the count of session subscribers for a given session (for diagnostics/tests).
    /// </summary>
    public int GetSessionSubscriberCount(string sessionId)
    {
        return _sessionSubs.TryGetValue(sessionId, out var subs) ? subs.Count : 0;
    }

    /// <summary>
    /// Returns the count of fleet-wide subscribers (for diagnostics/tests).
    /// </summary>
    public int GetFleetSubscriberCount()
    {
        return _fleetSubs.Count;
    }
}
