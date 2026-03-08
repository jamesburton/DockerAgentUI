using AgentHub.Contracts;
using AgentHub.Orchestration.Data;
using AgentHub.Orchestration.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentHub.Orchestration.Monitoring;

/// <summary>
/// BackgroundService that monitors running sessions for heartbeat timeouts.
/// Marks sessions as Failed when no heartbeat/event is received within the timeout period.
/// Detects orphaned sessions (3 missed heartbeats at 30s interval = 90s default timeout).
/// </summary>
public sealed class SessionMonitorService : BackgroundService
{
    private readonly IDbContextFactory<AgentHubDbContext> _dbFactory;
    private readonly ILogger<SessionMonitorService> _logger;
    private readonly TimeSpan _checkInterval;
    private readonly TimeSpan _heartbeatTimeout;

    public SessionMonitorService(
        IDbContextFactory<AgentHubDbContext> dbFactory,
        ILogger<SessionMonitorService> logger,
        TimeSpan? checkInterval = null,
        TimeSpan? heartbeatTimeout = null)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _checkInterval = checkInterval ?? TimeSpan.FromSeconds(30);
        _heartbeatTimeout = heartbeatTimeout ?? TimeSpan.FromSeconds(90);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "SessionMonitorService started (interval: {Interval}s, timeout: {Timeout}s)",
            _checkInterval.TotalSeconds, _heartbeatTimeout.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during session monitoring cycle");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    /// <summary>
    /// Run a single monitoring cycle. Exposed for testing.
    /// Scans for running sessions and checks last event timestamps.
    /// Sessions with no events within the heartbeat timeout are marked as Failed.
    /// </summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var cutoff = DateTimeOffset.UtcNow - _heartbeatTimeout;

        // Find running sessions
        var runningSessions = await db.Sessions
            .Where(s => s.State == SessionState.Running)
            .ToListAsync(ct);

        foreach (var session in runningSessions)
        {
            // Find the most recent event for this session
            var lastEventTime = await db.Events
                .Where(e => e.SessionId == session.SessionId)
                .OrderByDescending(e => e.TsUtc)
                .Select(e => (DateTimeOffset?)e.TsUtc)
                .FirstOrDefaultAsync(ct);

            // Use session creation time if no events exist
            var lastActivityTime = lastEventTime ?? session.CreatedUtc;

            if (lastActivityTime < cutoff)
            {
                _logger.LogWarning(
                    "Session {SessionId} marked as Failed: no heartbeat for {Elapsed}s (timeout: {Timeout}s)",
                    session.SessionId,
                    (DateTimeOffset.UtcNow - lastActivityTime).TotalSeconds,
                    _heartbeatTimeout.TotalSeconds);

                session.State = SessionState.Failed;
                session.CompletedUtc = DateTimeOffset.UtcNow;
                session.CleanupState = "pending";

                // Add a failure event
                db.Events.Add(new SessionEventEntity
                {
                    SessionId = session.SessionId,
                    Kind = SessionEventKind.StateChanged,
                    TsUtc = DateTimeOffset.UtcNow,
                    Data = "Failed (heartbeat timeout)"
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
