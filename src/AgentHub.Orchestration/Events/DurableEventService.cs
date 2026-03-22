using System.Collections.Concurrent;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AgentHub.Contracts;
using AgentHub.Orchestration.Data;
using AgentHub.Orchestration.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgentHub.Orchestration.Events;

/// <summary>
/// Durable event bus: persists events to DB, broadcasts to live SSE subscribers, replays on reconnect.
/// Registered as singleton; uses IDbContextFactory for scoped DB access.
/// </summary>
public sealed class DurableEventService
{
    private readonly IDbContextFactory<AgentHubDbContext> _dbFactory;
    private readonly SseSubscriptionManager _subs;

    /// <summary>
    /// Parent cache: childSessionId -> parentSessionId (null means "looked up, no parent").
    /// Lazily populated from DB on first lookup per child.
    /// </summary>
    private readonly ConcurrentDictionary<string, string?> _parentCache = new();

    /// <summary>
    /// Event kinds considered "lifecycle" for forwarding purposes.
    /// These are always forwarded to parent regardless of includeChildren setting.
    /// </summary>
    private static readonly HashSet<SessionEventKind> LifecycleKinds = new()
    {
        SessionEventKind.ChildSpawned,
        SessionEventKind.ChildCompleted,
        SessionEventKind.ChildFailed,
        SessionEventKind.StateChanged
    };

    public DurableEventService(IDbContextFactory<AgentHubDbContext> dbFactory, SseSubscriptionManager subs)
    {
        _dbFactory = dbFactory;
        _subs = subs;
    }

    /// <summary>
    /// Persists an event to the database and broadcasts to live SSE subscribers.
    /// Also forwards child events to parent session subscribers.
    /// </summary>
    public async Task EmitAsync(SessionEvent ev)
    {
        string eventId;

        if (string.IsNullOrEmpty(ev.SessionId))
        {
            // Host-level events (metrics, inventory) have no session -- broadcast only, skip DB
            eventId = Guid.NewGuid().ToString();
        }
        else
        {
            // Session events: persist to DB
            await using var db = await _dbFactory.CreateDbContextAsync();
            var entity = ev.ToEntity();
            db.Events.Add(entity);
            await db.SaveChangesAsync();
            eventId = entity.Id.ToString();
        }

        // Broadcast to live subscribers
        var sseItem = new SseItem<SessionEvent>(ev, "sessionEvent")
        {
            EventId = eventId
        };

        _subs.BroadcastToSession(ev.SessionId, sseItem);
        _subs.BroadcastToFleet(sseItem);

        // Forward child events to parent's SSE stream
        await ForwardToParentAsync(ev);
    }

    /// <summary>
    /// Forwards child session events to parent session subscribers.
    /// </summary>
    private async Task ForwardToParentAsync(SessionEvent ev)
    {
        if (string.IsNullOrEmpty(ev.SessionId))
            return;

        // Loop prevention: skip events already marked as forwarded
        if (ev.Meta?.TryGetValue("forwarded", out var fwd) == true && fwd == "true")
            return;

        // Look up parent from cache (or DB on miss)
        var parentId = await GetParentIdAsync(ev.SessionId);
        if (parentId is null)
            return;

        var isLifecycle = LifecycleKinds.Contains(ev.Kind);

        // Create prefixed event with forwarding metadata
        var forwardedMeta = new Dictionary<string, string>(ev.Meta ?? new())
        {
            ["forwarded"] = "true",
            ["sourceSessionId"] = ev.SessionId
        };

        var prefixedEvent = new SessionEvent(
            parentId,
            ev.Kind,
            ev.TsUtc,
            $"[{ev.SessionId}] {ev.Data}",
            forwardedMeta);

        var prefixedSseItem = new SseItem<SessionEvent>(prefixedEvent, "sessionEvent")
        {
            EventId = Guid.NewGuid().ToString()
        };

        _subs.BroadcastToParent(parentId, prefixedSseItem, isLifecycle);
    }

    /// <summary>
    /// Gets the parent session ID for a child, using cache with lazy DB lookup.
    /// </summary>
    private async Task<string?> GetParentIdAsync(string childSessionId)
    {
        if (_parentCache.TryGetValue(childSessionId, out var cached))
            return cached;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var parentId = await db.Sessions
            .Where(s => s.SessionId == childSessionId)
            .Select(s => s.ParentSessionId)
            .FirstOrDefaultAsync();

        _parentCache[childSessionId] = parentId;
        return parentId;
    }

    /// <summary>
    /// Invalidates the parent cache for a session (called when session completes/fails).
    /// </summary>
    public void InvalidateParentCache(string sessionId)
    {
        _parentCache.TryRemove(sessionId, out _);
    }

    /// <summary>
    /// Subscribes to a session's event stream. If lastEventId is provided, replays missed events from DB first.
    /// </summary>
    public async IAsyncEnumerable<SseItem<SessionEvent>> SubscribeSession(
        string sessionId,
        string? lastEventId,
        [EnumeratorCancellation] CancellationToken ct,
        bool includeChildren = false)
    {
        // Replay missed events from DB
        if (long.TryParse(lastEventId, out var afterId))
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var missed = await db.Events
                .Where(e => e.SessionId == sessionId && e.Id > afterId)
                .OrderBy(e => e.Id)
                .ToListAsync(ct);

            foreach (var e in missed)
            {
                yield return new SseItem<SessionEvent>(e.ToDto(), "sessionEvent")
                {
                    EventId = e.Id.ToString()
                };
            }
        }

        // Stream live events via channel
        var ch = Channel.CreateUnbounded<SseItem<SessionEvent>>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        var connectionId = Guid.NewGuid();
        _subs.AddSessionSubscriber(sessionId, connectionId, ch.Writer, includeChildren);

        try
        {
            await foreach (var item in ch.Reader.ReadAllAsync(ct))
            {
                yield return item;
            }
        }
        finally
        {
            _subs.RemoveSessionSubscriber(sessionId, connectionId);
            ch.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Subscribes to the fleet-wide event stream. If lastEventId is provided, replays missed events from DB first.
    /// </summary>
    public async IAsyncEnumerable<SseItem<SessionEvent>> SubscribeFleet(
        string? lastEventId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Replay missed events from DB
        if (long.TryParse(lastEventId, out var afterId))
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var missed = await db.Events
                .Where(e => e.Id > afterId)
                .OrderBy(e => e.Id)
                .ToListAsync(ct);

            foreach (var e in missed)
            {
                yield return new SseItem<SessionEvent>(e.ToDto(), "sessionEvent")
                {
                    EventId = e.Id.ToString()
                };
            }
        }

        // Stream live events via channel
        var ch = Channel.CreateUnbounded<SseItem<SessionEvent>>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        var connectionId = Guid.NewGuid();
        _subs.AddFleetSubscriber(connectionId, ch.Writer);

        try
        {
            await foreach (var item in ch.Reader.ReadAllAsync(ct))
            {
                yield return item;
            }
        }
        finally
        {
            _subs.RemoveFleetSubscriber(connectionId);
            ch.Writer.TryComplete();
        }
    }
}
