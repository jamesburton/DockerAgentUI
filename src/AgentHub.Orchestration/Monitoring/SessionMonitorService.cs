using AgentHub.Orchestration.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentHub.Orchestration.Monitoring;

/// <summary>
/// BackgroundService that monitors running sessions for heartbeat timeouts.
/// Marks sessions as Failed when no heartbeat/event is received within the timeout period.
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
        // Stub -- will be implemented in GREEN phase
        await Task.CompletedTask;
    }

    /// <summary>
    /// Run a single monitoring cycle. Exposed for testing.
    /// </summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        // Stub -- will be implemented in GREEN phase
        await Task.CompletedTask;
    }
}
