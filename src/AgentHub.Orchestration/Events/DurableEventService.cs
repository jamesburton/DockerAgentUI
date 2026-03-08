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

    public DurableEventService(IDbContextFactory<AgentHubDbContext> dbFactory, SseSubscriptionManager subs)
    {
        _dbFactory = dbFactory;
        _subs = subs;
    }

    /// <summary>
    /// Persists an event to the database and broadcasts to live SSE subscribers.
    /// </summary>
    public async Task EmitAsync(SessionEvent ev)
    {
        // 1. Persist to DB
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = ev.ToEntity();
        db.Events.Add(entity);
        await db.SaveChangesAsync();

        // 2. Broadcast to live subscribers with DB-assigned ID
        var sseItem = new SseItem<SessionEvent>(ev, "sessionEvent")
        {
            EventId = entity.Id.ToString()
        };

        _subs.BroadcastToSession(ev.SessionId, sseItem);
        _subs.BroadcastToFleet(sseItem);
    }

    /// <summary>
    /// Subscribes to a session's event stream. If lastEventId is provided, replays missed events from DB first.
    /// </summary>
    public async IAsyncEnumerable<SseItem<SessionEvent>> SubscribeSession(
        string sessionId,
        string? lastEventId,
        [EnumeratorCancellation] CancellationToken ct)
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
        _subs.AddSessionSubscriber(sessionId, connectionId, ch.Writer);

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
