using AgentHub.Contracts;
using AgentHub.Orchestration;
using AgentHub.Orchestration.Agents;
using AgentHub.Orchestration.Backends;
using AgentHub.Orchestration.Config;
using AgentHub.Orchestration.Coordinator;
using AgentHub.Orchestration.Events;
using AgentHub.Orchestration.Monitoring;
using AgentHub.Orchestration.Placement;
using AgentHub.Orchestration.Security;
using AgentHub.Orchestration.Storage;
using AgentHub.Orchestration.Worktree;
using AgentHub.Orchestration.Data;
using AgentHub.Orchestration.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

var rootPath = builder.Environment.ContentRootPath;
var configRoot = Path.GetFullPath(Path.Combine(rootPath, "..", "..", "config"));

// EF Core with SQLite persistence
var connectionString = builder.Configuration.GetConnectionString("AgentHub")
    ?? "Data Source=agenthub.db;Cache=Shared";
builder.Services.AddDbContextPool<AgentHubDbContext>(options =>
    options.UseSqlite(connectionString));
builder.Services.AddDbContextFactory<AgentHubDbContext>(options =>
    options.UseSqlite(connectionString), ServiceLifetime.Singleton);

// Host seeding from hosts.json into DB on startup
var hostsJsonPath = Path.Combine(configRoot, "hosts.json");
builder.Services.AddSingleton<IHostedService>(sp =>
    new HostSeedingService(
        sp.GetRequiredService<IDbContextFactory<AgentHubDbContext>>(),
        hostsJsonPath,
        sp.GetRequiredService<ILogger<HostSeedingService>>()));

builder.Services.AddSingleton<IUserContext, DevUserContext>();
builder.Services.AddSingleton<IPlacementEngine, SimplePlacementEngine>();
builder.Services.AddSingleton<ISanitizationService, BasicSanitizationService>();
builder.Services.AddSingleton<ISkillRegistry>(_ => new JsonSkillRegistry(Path.Combine(configRoot, "skills")));
builder.Services.AddSingleton<ISkillPolicyService>(_ => new JsonSkillPolicyService(Path.Combine(configRoot, "policies", "global.policy.json")));

// Host registry backed by DB (seeded from hosts.json by HostSeedingService)
builder.Services.AddSingleton<IHostRegistry>(sp =>
    new DbHostRegistry(sp.GetRequiredService<IDbContextFactory<AgentHubDbContext>>()));

builder.Services.AddSingleton<ISharedStorageProvider, BlobSharedStorageProvider>();
builder.Services.AddSingleton<IWorktreeProvider, GitWorktreeProvider>();

// Phase 2 services
builder.Services.AddSingleton<ApprovalService>();
builder.Services.AddSingleton<IHostedService>(sp =>
    new SessionMonitorService(
        sp.GetRequiredService<IDbContextFactory<AgentHubDbContext>>(),
        sp.GetRequiredService<ILogger<SessionMonitorService>>()));

// Host metric cache: shared in-memory cache for placement engine
builder.Services.AddSingleton<HostMetricCache>();

// Host metric polling: SSHs into hosts every 30s to collect CPU/memory stats
builder.Services.AddHostedService<HostMetricPollingService>();

// Host inventory polling: SSHs into hosts every 60min to discover agent CLIs, disk, git
builder.Services.AddSingleton<HostInventoryPollingService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<HostInventoryPollingService>());

builder.Services.AddSingleton<ISshHostConnectionFactory>(sp =>
{
    var factory = new SshHostConnectionFactory(sp.GetService<ILoggerFactory>());
    factory.DaemonPath = builder.Configuration["Ssh:DaemonPath"];
    return factory;
});
builder.Services.AddSingleton<AgentHub.Orchestration.Worktree.WorktreeService>();

builder.Services.AddSingleton<ISessionBackend, InMemoryBackend>();
builder.Services.AddSingleton<ISessionBackend, SshBackend>();
builder.Services.AddSingleton<ISessionCoordinator, SessionCoordinator>();

// Durable event service: persists events to DB, broadcasts to live SSE subscribers
builder.Services.AddSingleton<SseSubscriptionManager>();
builder.Services.AddSingleton<DurableEventService>();

// Agent adapter
builder.Services.AddSingleton<IAgentAdapter, ClaudeCodeAdapter>();

var app = builder.Build();

// Apply migrations and set SQLite PRAGMAs
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AgentHubDbContext>();
    db.Database.Migrate();
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    db.Database.ExecuteSqlRaw("PRAGMA busy_timeout=5000;");
}

app.MapGet("/healthz", () => Results.Ok(new { ok = true }));

app.MapGet("/api/hosts", async (IHostRegistry hosts, CancellationToken ct)
    => Results.Ok(await hosts.ListAsync(ct)));

// On-demand inventory refresh: single host
app.MapPost("/api/hosts/{hostId}/refresh-inventory", async (
    string hostId,
    HostInventoryPollingService inventoryService,
    CancellationToken ct) =>
{
    await inventoryService.RefreshHostAsync(hostId, ct);
    return Results.Accepted();
});

// On-demand inventory refresh: all hosts
app.MapPost("/api/hosts/refresh-inventory", async (
    HostInventoryPollingService inventoryService,
    CancellationToken ct) =>
{
    await inventoryService.RunOnceAsync(ct);
    return Results.Accepted();
});

app.MapGet("/api/skills", (ISkillRegistry skills)
    => Results.Ok(skills.GetAll()));

app.MapGet("/api/policy", (ISkillPolicyService policy)
    => Results.Ok(policy.GetPolicySnapshot()));

// Session listing with pagination and state filtering
app.MapGet("/api/sessions", async (IUserContext user, ISessionCoordinator coordinator,
    int? skip, int? take, string? state, CancellationToken ct) =>
{
    var (items, totalCount) = await coordinator.GetSessionHistoryAsync(
        user.UserId, skip ?? 0, take ?? 50, state, ct);
    return Results.Json(new { items, totalCount });
});

app.MapGet("/api/sessions/{sessionId}", async (string sessionId, IUserContext user, ISessionCoordinator coordinator, CancellationToken ct) =>
{
    var s = await coordinator.GetSessionAsync(sessionId, user.UserId, ct);
    return s is null ? Results.NotFound() : Results.Ok(s);
});

// Session history: full event replay with pagination, kind filtering, typed DTOs
app.MapGet("/api/sessions/{sessionId}/history", async (string sessionId, IUserContext user, ISessionCoordinator coordinator,
    IDbContextFactory<AgentHubDbContext> dbFactory, int? page, int? pageSize, string? kind, CancellationToken ct) =>
{
    var s = await coordinator.GetSessionAsync(sessionId, user.UserId, ct);
    if (s is null) return Results.NotFound();

    await using var db = await dbFactory.CreateDbContextAsync(ct);
    var rawEvents = await db.Events
        .Where(e => e.SessionId == sessionId)
        .ToListAsync(ct);

    // In-memory sort (Phase 2 SQLite DateTimeOffset limitation)
    var sorted = rawEvents.OrderBy(e => e.TsUtc).AsEnumerable();

    // Kind filter (comma-separated, case-insensitive per locked decision)
    if (!string.IsNullOrEmpty(kind))
    {
        var kindValues = kind.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var parsedKinds = new List<SessionEventKind>();
        foreach (var k in kindValues)
        {
            if (!Enum.TryParse<SessionEventKind>(k, ignoreCase: true, out var parsed))
                return Results.BadRequest(new { error = $"Invalid kind value: '{k}'" });
            parsedKinds.Add(parsed);
        }
        sorted = sorted.Where(e => parsedKinds.Contains(e.Kind));
    }

    var filtered = sorted.ToList();
    var totalCount = filtered.Count;
    var p = page ?? 1;
    var ps = Math.Clamp(pageSize ?? 100, 1, 500);
    var items = filtered
        .Skip((p - 1) * ps)
        .Take(ps)
        .Select(e => e.ToDto())
        .ToList();

    return Results.Json(new { items, totalCount }, HistoryJson.Options);
});

app.MapPost("/api/sessions", async (StartSessionRequest req, IUserContext user, ISessionCoordinator coordinator, DurableEventService events, ILogger<Program> logger, CancellationToken ct) =>
{
    try
    {
        var sessionId = await coordinator.StartSessionAsync(user.UserId, req, events.EmitAsync, ct);
        return Results.Created($"/api/sessions/{sessionId}", new { sessionId });
    }
    catch (InvalidOperationException ex)
    {
        logger.LogWarning(ex, "Session start rejected: {Message}", ex.Message);
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (System.Net.Sockets.SocketException ex)
    {
        logger.LogError(ex, "Network error starting session: {Message}", ex.Message);
        return Results.Json(new { error = $"Cannot reach host: {ex.Message}" }, statusCode: 502);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        logger.LogError(ex, "Unexpected error starting session: {Message}", ex.Message);
        return Results.Json(new { error = $"Session start failed: {ex.Message}" }, statusCode: 500);
    }
});

app.MapPost("/api/sessions/{sessionId}/input", async (string sessionId, SendInputRequest req, IUserContext user, ISessionCoordinator coordinator, DurableEventService events, CancellationToken ct) =>
{
    var delivered = await coordinator.SendInputAsync(user.UserId, sessionId, req, events.EmitAsync, ct);
    return Results.Ok(new { delivered });
});

// Graceful or force stop via DELETE
app.MapDelete("/api/sessions/{sessionId}", async (string sessionId, bool? force, IUserContext user, ISessionCoordinator coordinator, CancellationToken ct) =>
{
    await coordinator.StopSessionAsync(user.UserId, sessionId, forceKill: force ?? false, ct);
    return Results.Accepted();
});

// Approval resolution endpoint
app.MapPost("/api/approvals/{approvalId}/resolve", async (string approvalId, ApprovalResolveRequest req, ApprovalService approvalService) =>
{
    approvalService.ResolveApproval(approvalId, req.Approved, req.ResolvedBy);
    return Results.Ok();
});

app.MapGet("/api/sessions/{sessionId}/events", async (
    string sessionId,
    [Microsoft.AspNetCore.Mvc.FromHeader(Name = "Last-Event-ID")] string? lastEventId,
    IUserContext user,
    ISessionCoordinator coordinator,
    DurableEventService events,
    CancellationToken ct) =>
{
    var s = await coordinator.GetSessionAsync(sessionId, user.UserId, ct);
    if (s is null) return Results.NotFound();
    return TypedResults.ServerSentEvents(
        events.SubscribeSession(sessionId, lastEventId, ct),
        eventType: "sessionEvent");
});

app.MapGet("/api/events", (
    [Microsoft.AspNetCore.Mvc.FromHeader(Name = "Last-Event-ID")] string? lastEventId,
    DurableEventService events,
    CancellationToken ct) =>
{
    return TypedResults.ServerSentEvents(
        events.SubscribeFleet(lastEventId, ct),
        eventType: "fleetEvent");
});

// GET /api/sessions/{sessionId}/diff - Get diff stats for a worktree session
app.MapGet("/api/sessions/{sessionId}/diff", async (
    string sessionId,
    IUserContext user,
    ISessionCoordinator coordinator,
    WorktreeService worktreeService,
    ISshHostConnectionFactory connectionFactory,
    IHostRegistry hostRegistry,
    IConfiguration configuration,
    CancellationToken ct) =>
{
    var session = await coordinator.GetSessionAsync(sessionId, user.UserId, ct);
    if (session is null) return Results.NotFound();
    if (session.WorktreeBranch is null) return Results.BadRequest(new { error = "Session is not a worktree session" });

    var host = await hostRegistry.GetAsync(session.Node!, ct);
    if (host?.Address is null) return Results.BadRequest(new { error = "Host not reachable" });

    var sshKeyPath = configuration["Ssh:PrivateKeyPath"]
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "id_rsa");
    var sshUsername = configuration["Ssh:Username"] ?? "agent-user";

    var hostAddress = host.Address.Replace("ssh://", "");
    await using var connection = connectionFactory.Create(hostAddress, sshUsername, sshKeyPath);
    await connection.ConnectAsync(ct);

    var repoRoot = (await connection.ExecuteCommandAsync("git rev-parse --show-toplevel 2>/dev/null", ct)).Trim();
    if (string.IsNullOrEmpty(repoRoot)) return Results.BadRequest(new { error = "Cannot determine repo root" });

    var diff = await worktreeService.GetDiffStatsAsync(connection, repoRoot, session.WorktreeBranch, ct);
    return Results.Ok(diff);
});

// POST /api/hosts/{hostId}/worktree-cleanup - Remove orphaned worktrees on a host
app.MapPost("/api/hosts/{hostId}/worktree-cleanup", async (
    string hostId,
    WorktreeService worktreeService,
    ISshHostConnectionFactory connectionFactory,
    IHostRegistry hostRegistry,
    ISessionCoordinator coordinator,
    IUserContext user,
    IConfiguration configuration,
    CancellationToken ct) =>
{
    var host = await hostRegistry.GetAsync(hostId, ct);
    if (host?.Address is null) return Results.NotFound();

    var sshKeyPath = configuration["Ssh:PrivateKeyPath"]
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "id_rsa");
    var sshUsername = configuration["Ssh:Username"] ?? "agent-user";

    var hostAddress = host.Address.Replace("ssh://", "");
    await using var connection = connectionFactory.Create(hostAddress, sshUsername, sshKeyPath);
    await connection.ConnectAsync(ct);

    var repoRoot = (await connection.ExecuteCommandAsync("git rev-parse --show-toplevel 2>/dev/null", ct)).Trim();
    if (string.IsNullOrEmpty(repoRoot)) return Results.BadRequest(new { error = "Cannot determine repo root" });

    // Get active session IDs for this host
    var sessions = await coordinator.ListSessionsAsync(user.UserId, ct);
    var activeIds = sessions
        .Where(s => s.Node == hostId && s.State == SessionState.Running)
        .Select(s => s.SessionId)
        .ToList();

    var orphans = await worktreeService.FindOrphanedWorktreesAsync(connection, repoRoot, activeIds, ct);
    if (orphans.Count == 0) return Results.Ok(new { cleaned = 0, message = "No orphaned worktrees found" });

    await worktreeService.CleanupOrphanedAsync(connection, repoRoot, orphans, ct);
    return Results.Ok(new { cleaned = orphans.Count, paths = orphans });
});

app.Run();

// JSON options for history endpoint (enum-as-string, camelCase)
static partial class HistoryJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
}

// Make the auto-generated Program class accessible to integration tests
public partial class Program { }

public interface IUserContext
{
    string UserId { get; }
}

public sealed class DevUserContext : IUserContext
{
    public string UserId => "dev-user";
}

/// <summary>
/// Request body for approval resolution endpoint.
/// </summary>
public sealed record ApprovalResolveRequest(bool Approved, string? ResolvedBy = null);
