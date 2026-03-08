using System.Collections.Concurrent;
using System.Threading.Channels;
using AgentHub.Contracts;
using AgentHub.Orchestration;
using AgentHub.Orchestration.Backends;
using AgentHub.Orchestration.Config;
using AgentHub.Orchestration.Coordinator;
using AgentHub.Orchestration.Placement;
using AgentHub.Orchestration.Security;
using AgentHub.Orchestration.Storage;
using AgentHub.Orchestration.Data;
using Microsoft.EntityFrameworkCore;

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

builder.Services.AddSingleton<ISessionBackend, InMemoryBackend>();
builder.Services.AddSingleton<ISessionBackend, SshBackend>();
builder.Services.AddSingleton<ISessionCoordinator, SessionCoordinator>();

builder.Services.AddSingleton<SessionEventBus>();

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

app.MapGet("/api/skills", (ISkillRegistry skills)
    => Results.Ok(skills.GetAll()));

app.MapGet("/api/policy", (ISkillPolicyService policy)
    => Results.Ok(policy.GetPolicySnapshot()));

app.MapGet("/api/sessions", async (IUserContext user, ISessionCoordinator coordinator, CancellationToken ct)
    => Results.Ok(await coordinator.ListSessionsAsync(user.UserId, ct)));

app.MapGet("/api/sessions/{sessionId}", async (string sessionId, IUserContext user, ISessionCoordinator coordinator, CancellationToken ct) =>
{
    var s = await coordinator.GetSessionAsync(sessionId, user.UserId, ct);
    return s is null ? Results.NotFound() : Results.Ok(s);
});

app.MapPost("/api/sessions", async (StartSessionRequest req, IUserContext user, ISessionCoordinator coordinator, SessionEventBus bus, CancellationToken ct) =>
{
    var sessionId = await coordinator.StartSessionAsync(user.UserId, req, bus.EmitAsync, ct);
    return Results.Created($"/api/sessions/{sessionId}", new { sessionId });
});

app.MapPost("/api/sessions/{sessionId}/input", async (string sessionId, SendInputRequest req, IUserContext user, ISessionCoordinator coordinator, SessionEventBus bus, CancellationToken ct) =>
{
    await coordinator.SendInputAsync(user.UserId, sessionId, req, bus.EmitAsync, ct);
    return Results.Accepted();
});

app.MapPost("/api/sessions/{sessionId}/stop", async (string sessionId, IUserContext user, ISessionCoordinator coordinator, CancellationToken ct) =>
{
    await coordinator.StopSessionAsync(user.UserId, sessionId, ct);
    return Results.Accepted();
});

app.MapGet("/api/sessions/{sessionId}/events", async (string sessionId, IUserContext user, ISessionCoordinator coordinator, SessionEventBus bus, CancellationToken ct) =>
{
    var s = await coordinator.GetSessionAsync(sessionId, user.UserId, ct);
    if (s is null) return Results.NotFound();
    var stream = bus.Subscribe(sessionId, ct);
    return TypedResults.ServerSentEvents(stream, eventType: "sessionEvent");
});

app.Run();

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

public sealed class SessionEventBus
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<ChannelWriter<SessionEvent>>> _subs = new();

    public Task EmitAsync(SessionEvent ev)
    {
        if (_subs.TryGetValue(ev.SessionId, out var bag))
        {
            foreach (var writer in bag)
                writer.TryWrite(ev);
        }

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<SessionEvent> Subscribe(string sessionId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var ch = Channel.CreateUnbounded<SessionEvent>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        var bag = _subs.GetOrAdd(sessionId, _ => new ConcurrentBag<ChannelWriter<SessionEvent>>());
        bag.Add(ch.Writer);

        ch.Writer.TryWrite(new SessionEvent(sessionId, SessionEventKind.Info, DateTimeOffset.UtcNow, "SSE connected"));

        try
        {
            await foreach (var ev in ch.Reader.ReadAllAsync(ct))
                yield return ev;
        }
        finally
        {
            ch.Writer.TryComplete();
        }
    }
}
