# Phase 1: Foundation and Event Infrastructure - Research

**Researched:** 2026-03-08
**Domain:** ASP.NET Core API, EF Core persistence, SSE streaming, agent adapter pattern
**Confidence:** HIGH

## Summary

Phase 1 establishes the durable foundation for AgentSafeEnv: a coordinator REST API backed by EF Core with SQLite, real-time SSE event streaming with replay support, and an agent adapter abstraction with Claude Code as the first implementation. The existing scaffold provides well-designed DTOs and interfaces (Models.cs, Abstractions.cs) but all implementations are stubs with in-memory state that must be replaced with persistent storage.

The project targets .NET 10 (SDK 10.0.103 installed) which provides native SSE support via `TypedResults.ServerSentEvents` and `SseItem<T>` -- a significant improvement over the previous approach of manually formatting SSE responses. EF Core 10 with SQLite is the persistence layer, and CliWrap 3.10.0 is the standard library for wrapping the Claude Code CLI process.

**Primary recommendation:** Rewrite all implementations against the existing interfaces, adding EF Core persistence for sessions/events/hosts, upgrading the in-memory SessionEventBus to a durable event store with SSE replay, and introducing an IAgentAdapter interface with a Claude Code CLI adapter using CliWrap.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- Claude evaluates each piece of the existing scaffold against Phase 1 requirements
- Existing contracts/models (SessionSummary, SessionEvent, HostRecord, SkillManifest, enums) are a solid starting point -- refine as needed
- Existing interfaces (ISessionBackend, ISessionCoordinator, etc.) are well-structured abstractions -- keep what serves Phase 1, rewrite all implementations
- 5-project solution structure (Contracts, Orchestration, Service, AppHost, Maui) -- Claude decides if this is the right split or needs adjustment
- EF Core with SQLite as first provider, pluggable for future swap to Postgres/SQL Server/SpacetimeDB
- What persists: Sessions (active + history), events/output, host inventory
- Config on disk: Skills, policies, agent definitions stay as files on disk (JSON/MD)
- Config must be copyable between servers, hosts, and sessions
- Host inventory: Seed from hosts.json on startup, track runtime state in DB
- SSE with event IDs and Last-Event-ID replay support
- Two stream types: Per-session (/api/sessions/{id}/events) AND fleet-wide (/api/events) for dashboard use
- Replace or evolve the existing in-memory SessionEventBus to support durable event persistence
- Claude Code as first adapter: Standard CLI wrapping via CliWrap as default invocation method
- MCP connection also supported: Configuration assistant to set up MCP with minimal effort
- Permission flags: Both per-agent-type defaults AND per-session overrides
- Use --output-format json where agents support it. Version-aware adapters

### Claude's Discretion
- Whether to get the scaffold compiling first or build incrementally from scratch
- Which existing interfaces to keep vs redesign
- Project structure adjustments
- EF Core entity design and migration strategy
- Event storage approach (all-in-DB vs hybrid)
- Event type extensions beyond current enum
- SSE replay depth defaults
- Whether IAgentAdapter is a separate interface from ISessionBackend

### Deferred Ideas (OUT OF SCOPE)
- SpacetimeDB as an EF Core provider alternative
- Database-backed config persistence (skills, policies, agent definitions)
- MCP as primary agent control protocol (vs CLI wrapping)
- Config hot-reload / notification system
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| INFRA-01 | Coordinator exposes REST API for session CRUD, host listing, and event streaming | Existing Minimal API endpoints in Program.cs provide the pattern; add fleet-wide SSE endpoint and persist state via EF Core |
| INFRA-02 | Lightweight host daemon runs on each target machine to receive commands and report status | Define the host daemon command protocol (receivable commands, status reporting); stub implementation is sufficient per success criteria |
| INFRA-03 | Session state persists in a durable data store (EF Core with pluggable provider) | EF Core 10 with SQLite provider; entity design for sessions, events, hosts; migration strategy documented below |
| MON-01 | User can stream real-time agent output via SSE as it happens | .NET 10 native SSE via TypedResults.ServerSentEvents with SseItem<T> for event IDs; Last-Event-ID replay via event buffer backed by DB |
| AGENT-01 | System supports multiple agent types via adapter pattern (Claude, Codex, Copilot, Gemini, OpenCode) | IAgentAdapter interface separating agent concerns from backend/transport; Claude Code adapter using CliWrap with --output-format stream-json |
</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| ASP.NET Core (Minimal API) | .NET 10.0 | REST API and SSE endpoints | Native framework, first-class SSE support via TypedResults.ServerSentEvents |
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.x | Persistent storage for sessions, events, hosts | Official EF Core provider, pluggable design allows swap to Postgres later |
| Microsoft.EntityFrameworkCore.Design | 10.0.x | EF Core migrations tooling | Required for `dotnet ef migrations` commands |
| CliWrap | 3.10.0 | Wrapping Claude Code CLI process | De facto .NET library for process management; async, fluent, safe |
| System.Threading.Channels | in-box | In-process event pub/sub for SSE | Already used in scaffold; high-performance async producer-consumer |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| System.Net.ServerSentEvents | in-box (.NET 10) | SseItem<T> types for SSE formatting | Used automatically by TypedResults.ServerSentEvents |
| System.Text.Json | in-box | JSON serialization (camelCase via JsonSerializerDefaults.Web) | All API responses and config file loading |
| Microsoft.Extensions.Hosting | in-box | Background services for event persistence worker | DurableEventService as IHostedService |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| SQLite | Postgres | Better concurrency but requires external server; swap later when >10 concurrent sessions |
| CliWrap | System.Diagnostics.Process | Process is low-level, error-prone for stream redirection; CliWrap handles deadlocks and cancellation |
| In-process Channels | Redis Streams | Redis adds external dependency; Channels are sufficient for single-coordinator Phase 1 |

**Installation:**
```bash
dotnet add src/AgentHub.Orchestration/AgentHub.Orchestration.csproj package Microsoft.EntityFrameworkCore.Sqlite --version 10.0.2
dotnet add src/AgentHub.Orchestration/AgentHub.Orchestration.csproj package Microsoft.EntityFrameworkCore.Design --version 10.0.2
dotnet add src/AgentHub.Orchestration/AgentHub.Orchestration.csproj package CliWrap --version 3.10.0
```

## Architecture Patterns

### Recommended Project Structure
```
src/
├── AgentHub.Contracts/          # DTOs, enums, request/response records (no dependencies)
│   ├── Models.cs                # Existing -- refine as needed
│   └── AgentTypes.cs            # NEW: agent type enum, adapter config records
├── AgentHub.Orchestration/      # Business logic, interfaces, implementations
│   ├── Abstractions.cs          # Existing interfaces -- keep ISessionCoordinator, IHostRegistry, etc.
│   ├── Agents/                  # NEW: IAgentAdapter + ClaudeCodeAdapter
│   ├── Backends/                # Existing -- rewrite to use EF Core
│   ├── Config/                  # Existing -- keep file-based config loaders
│   ├── Data/                    # NEW: EF Core DbContext, entities, migrations
│   ├── Events/                  # NEW: DurableEventService, EventBuffer for SSE replay
│   └── Coordinator/             # Existing -- evolve SessionCoordinator
├── AgentHub.Service/            # ASP.NET Core host (composition root)
│   └── Program.cs              # DI wiring, endpoint mapping
├── AgentHub.AppHost/            # Aspire orchestration (minimal for now)
└── AgentHub.Maui/               # Desktop client (not Phase 1)
```

### Pattern 1: EF Core Entity Design
**What:** Separate EF Core entities from API DTOs (the existing records in Models.cs)
**When to use:** Always -- keeps persistence concerns out of the API contract
**Example:**
```csharp
// Source: EF Core best practices for .NET 10
// Data/Entities/SessionEntity.cs
public class SessionEntity
{
    public string SessionId { get; set; } = string.Empty;
    public string OwnerUserId { get; set; } = string.Empty;
    public SessionState State { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public string Backend { get; set; } = string.Empty;
    public string? Node { get; set; }
    public string? AgentType { get; set; }
    public string RequirementsJson { get; set; } = "{}"; // Serialize complex record
    public string? WorktreePath { get; set; }
    public string? RiskAcceptedBy { get; set; }

    public List<SessionEventEntity> Events { get; set; } = [];
}

public class SessionEventEntity
{
    public long Id { get; set; }  // Auto-increment, used as SSE EventId
    public string SessionId { get; set; } = string.Empty;
    public SessionEventKind Kind { get; set; }
    public DateTimeOffset TsUtc { get; set; }
    public string Data { get; set; } = string.Empty;
    public string? MetaJson { get; set; }

    public SessionEntity Session { get; set; } = null!;
}

public class HostEntity
{
    public string HostId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Backend { get; set; } = string.Empty;
    public string Os { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public bool AllowSsh { get; set; }
    public string? LabelsJson { get; set; }
    public string? Address { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
    public string Status { get; set; } = "unknown"; // Runtime state
}
```

### Pattern 2: Durable Event Bus with SSE Replay
**What:** Events are persisted to DB on emit AND broadcast via Channels to live SSE subscribers. On reconnect, missed events are replayed from DB using Last-Event-ID.
**When to use:** All SSE endpoints
**Example:**
```csharp
// Source: .NET 10 SSE docs + Milan Jovanovic blog pattern
// Events/DurableEventService.cs
public class DurableEventService(AgentHubDbContext db)
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<ChannelWriter<SseItem<SessionEvent>>>> _sessionSubs = new();
    private readonly ConcurrentBag<ChannelWriter<SseItem<SessionEvent>>> _fleetSubs = new();

    public async Task EmitAsync(SessionEvent ev)
    {
        // 1. Persist to DB
        var entity = new SessionEventEntity
        {
            SessionId = ev.SessionId,
            Kind = ev.Kind,
            TsUtc = ev.TsUtc,
            Data = ev.Data,
            MetaJson = ev.Meta is not null ? JsonSerializer.Serialize(ev.Meta) : null
        };
        db.Events.Add(entity);
        await db.SaveChangesAsync();

        // 2. Broadcast to live subscribers with DB-assigned ID
        var sseItem = new SseItem<SessionEvent>(ev)
        {
            EventId = entity.Id.ToString()
        };

        // Per-session subscribers
        if (_sessionSubs.TryGetValue(ev.SessionId, out var bag))
            foreach (var writer in bag)
                writer.TryWrite(sseItem);

        // Fleet-wide subscribers
        foreach (var writer in _fleetSubs)
            writer.TryWrite(sseItem);
    }

    public async IAsyncEnumerable<SseItem<SessionEvent>> SubscribeSession(
        string sessionId, string? lastEventId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Replay missed events from DB
        if (long.TryParse(lastEventId, out var afterId))
        {
            var missed = await db.Events
                .Where(e => e.SessionId == sessionId && e.Id > afterId)
                .OrderBy(e => e.Id)
                .ToListAsync(ct);
            foreach (var e in missed)
                yield return ToSseItem(e);
        }

        // Then stream live
        var ch = Channel.CreateUnbounded<SseItem<SessionEvent>>();
        var subs = _sessionSubs.GetOrAdd(sessionId, _ => new());
        subs.Add(ch.Writer);
        try
        {
            await foreach (var item in ch.Reader.ReadAllAsync(ct))
                yield return item;
        }
        finally { ch.Writer.TryComplete(); }
    }
}
```

### Pattern 3: Agent Adapter Interface
**What:** Separates agent-type concerns (how to invoke Claude Code, Codex, etc.) from session backend concerns (where to run -- SSH, local, container)
**When to use:** When the system needs to support multiple agent types on the same backend
**Example:**
```csharp
// Agents/IAgentAdapter.cs
public interface IAgentAdapter
{
    string AgentType { get; }  // "claude-code", "codex", "copilot", etc.
    Task<AgentProcess> StartAsync(AgentStartRequest request, CancellationToken ct);
}

public record AgentStartRequest(
    string SessionId,
    string WorkingDirectory,
    string Prompt,
    Dictionary<string, string> Environment,
    AgentPermissions Permissions);

public record AgentPermissions(
    bool SkipPermissionPrompts,
    string[] AllowedTools,
    string[] DisallowedTools);

public record AgentProcess(
    IAsyncEnumerable<AgentOutputLine> Output,
    Func<string, Task> SendInput,
    Func<Task> Stop,
    Task Completion);

public record AgentOutputLine(
    AgentOutputKind Kind,  // StdOut, StdErr, Json, ToolUse, etc.
    string Content,
    DateTimeOffset Timestamp);
```

### Pattern 4: SSE Endpoint with Last-Event-ID
**What:** Read the Last-Event-ID header and pass it to the event service for replay
**When to use:** Both per-session and fleet-wide SSE endpoints
**Example:**
```csharp
// In Program.cs
app.MapGet("/api/sessions/{sessionId}/events", async (
    string sessionId,
    [FromHeader(Name = "Last-Event-ID")] string? lastEventId,
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
    [FromHeader(Name = "Last-Event-ID")] string? lastEventId,
    DurableEventService events,
    CancellationToken ct) =>
{
    return TypedResults.ServerSentEvents(
        events.SubscribeFleet(lastEventId, ct),
        eventType: "fleetEvent");
});
```

### Anti-Patterns to Avoid
- **In-memory-only state for sessions:** The existing InMemoryBackend and SshBackend use ConcurrentDictionary -- all session state is lost on restart. Must persist to EF Core.
- **Unbounded event buffers:** Without a replay depth limit, the in-memory buffer or DB query for replay can grow unbounded. Set a configurable max (e.g., 1000 events per session for replay, unlimited in DB).
- **Singleton DbContext:** EF Core DbContext must be scoped (per-request). Use `AddDbContext` or `AddDbContextPool`, never `AddSingleton`. For background services, use `IDbContextFactory`.
- **Blocking CLI calls:** CliWrap is async by design -- never use `.Wait()` or `.Result` on process output streams.
- **Tight coupling of agent type to backend:** Keep IAgentAdapter (what agent to run) separate from ISessionBackend (where/how to run). A Claude Code adapter should work on SSH, local, or container backends.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| CLI process management | Raw System.Diagnostics.Process | CliWrap 3.10.0 | Handles stdin/stdout deadlocks, async cancellation, pipe composition, exit code validation |
| SSE wire format | Manual `text/event-stream` response writing | TypedResults.ServerSentEvents + SseItem<T> | .NET 10 built-in; handles formatting, content-type, keep-alive |
| DB migrations | Manual SQL scripts | EF Core Migrations (`dotnet ef migrations`) | Schema versioning, rollback, provider-agnostic |
| JSON serialization config | Custom converters for camelCase | JsonSerializerDefaults.Web | Already used in scaffold; handles camelCase, number handling, null policies |
| Async producer-consumer | Manual lock-based queues | System.Threading.Channels | High-performance, back-pressure support, built into .NET |

**Key insight:** Every "deceptively simple" problem above has edge cases that take days to debug (process deadlocks, SSE reconnection races, migration conflicts). The listed libraries handle these correctly.

## Common Pitfalls

### Pitfall 1: SQLite Concurrency Under SSE Load
**What goes wrong:** SQLite uses file-level locking. Multiple concurrent SSE connections writing events can cause `SQLITE_BUSY` errors.
**Why it happens:** Each event emit writes to the DB; with 10+ simultaneous sessions streaming, write contention spikes.
**How to avoid:** Use WAL (Write-Ahead Logging) journal mode, set `PRAGMA busy_timeout` to a reasonable value (e.g., 5000ms), and consider batching event writes.
**Warning signs:** Intermittent `database is locked` exceptions under concurrent load.
```csharp
// In connection string or OnConfiguring:
optionsBuilder.UseSqlite("Data Source=agenthub.db;Cache=Shared",
    o => o.CommandTimeout(30));
// After context creation:
db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
db.Database.ExecuteSqlRaw("PRAGMA busy_timeout=5000;");
```

### Pitfall 2: SSE Connection Lifecycle Leaks
**What goes wrong:** ChannelWriter references accumulate in ConcurrentBag when clients disconnect without cleanup.
**Why it happens:** ConcurrentBag doesn't support removal; if the finally block doesn't execute cleanly, writers pile up.
**How to avoid:** Use ConcurrentDictionary keyed by connection ID instead of ConcurrentBag. Track subscriber count. Clean up in a periodic background task.
**Warning signs:** Memory growth over time, events being written to disposed channels.

### Pitfall 3: Claude Code CLI Output Format Instability
**What goes wrong:** Claude Code's `--output-format stream-json` emits NDJSON but the schema of individual events is not formally versioned and can change between releases.
**Why it happens:** Claude Code is a fast-moving tool; output structure changes without deprecation warnings.
**How to avoid:** Version-aware adapter design. Parse output defensively (unknown fields ignored, missing fields have defaults). Pin the Claude Code version in agent config. Log raw output alongside parsed output for debugging.
**Warning signs:** Deserialization failures after Claude Code updates, missing fields in parsed events.

### Pitfall 4: EF Core Entity vs DTO Confusion
**What goes wrong:** Using the same record types (SessionSummary, SessionEvent) as both EF Core entities and API DTOs creates circular dependencies and serialization issues.
**Why it happens:** Tempting to reuse existing well-designed records for persistence.
**How to avoid:** Separate entity classes in Data/Entities/ with explicit mapping methods to/from the existing contract records. Keep Models.cs as the API contract layer.
**Warning signs:** EF Core tracking errors, JSON serialization including navigation properties, tight coupling between persistence and API layers.

### Pitfall 5: Missing Host Seeding on First Run
**What goes wrong:** The system starts with an empty host database and no way to manage hosts.
**Why it happens:** Moving from file-only (hosts.json) to DB without a seeding strategy.
**How to avoid:** On startup, read hosts.json and upsert into the DB. File is the seed source; DB tracks runtime state (last seen, current status). Re-seeding on restart merges rather than overwrites.
**Warning signs:** Empty host list after fresh deployment, hosts disappearing after restart.

## Code Examples

### Claude Code Adapter with CliWrap
```csharp
// Source: CliWrap 3.10.0 docs + Claude Code CLI reference
public class ClaudeCodeAdapter : IAgentAdapter
{
    public string AgentType => "claude-code";

    public async Task<AgentProcess> StartAsync(AgentStartRequest request, CancellationToken ct)
    {
        var args = new List<string>
        {
            "-p", request.Prompt,
            "--output-format", "stream-json",
            "--no-session-persistence"
        };

        if (request.Permissions.SkipPermissionPrompts)
            args.Add("--dangerously-skip-permissions");

        if (request.Permissions.AllowedTools is { Length: > 0 })
        {
            args.Add("--allowedTools");
            args.AddRange(request.Permissions.AllowedTools);
        }

        var stdoutChannel = Channel.CreateUnbounded<AgentOutputLine>();

        var cmd = Cli.Wrap("claude")
            .WithArguments(args)
            .WithWorkingDirectory(request.WorkingDirectory)
            .WithEnvironmentVariables(env =>
            {
                foreach (var (k, v) in request.Environment)
                    env.Set(k, v);
            })
            .WithStandardOutputPipe(PipeTarget.ToDelegate(line =>
            {
                stdoutChannel.Writer.TryWrite(new AgentOutputLine(
                    AgentOutputKind.StdOut, line, DateTimeOffset.UtcNow));
            }))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(line =>
            {
                stdoutChannel.Writer.TryWrite(new AgentOutputLine(
                    AgentOutputKind.StdErr, line, DateTimeOffset.UtcNow));
            }));

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var task = cmd.ExecuteAsync(cts.Token);

        _ = task.Task.ContinueWith(_ => stdoutChannel.Writer.TryComplete());

        return new AgentProcess(
            Output: stdoutChannel.Reader.ReadAllAsync(ct),
            SendInput: _ => Task.CompletedTask, // -p mode is non-interactive
            Stop: () => { cts.Cancel(); return Task.CompletedTask; },
            Completion: task.Task);
    }
}
```

### DbContext Configuration
```csharp
// Data/AgentHubDbContext.cs
public class AgentHubDbContext(DbContextOptions<AgentHubDbContext> options)
    : DbContext(options)
{
    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();
    public DbSet<SessionEventEntity> Events => Set<SessionEventEntity>();
    public DbSet<HostEntity> Hosts => Set<HostEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SessionEntity>(e =>
        {
            e.HasKey(x => x.SessionId);
            e.HasIndex(x => x.OwnerUserId);
            e.HasIndex(x => x.State);
        });

        modelBuilder.Entity<SessionEventEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.SessionId, x.Id }); // For replay queries
            e.Property(x => x.Id).ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<HostEntity>(e =>
        {
            e.HasKey(x => x.HostId);
        });
    }
}
```

### DI Registration
```csharp
// In Program.cs
builder.Services.AddDbContextPool<AgentHubDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("AgentHub")
        ?? "Data Source=agenthub.db;Cache=Shared"));

builder.Services.AddScoped<DurableEventService>();
builder.Services.AddSingleton<IAgentAdapter, ClaudeCodeAdapter>();
builder.Services.AddHostedService<HostSeedingService>(); // Reads hosts.json, upserts to DB
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Manual SSE response formatting | TypedResults.ServerSentEvents + SseItem<T> | .NET 10 (Nov 2025) | No more manual content-type headers or text/event-stream formatting |
| Third-party SSE libraries (Lib.AspNetCore.ServerSentEvents) | Built-in .NET 10 SSE support | .NET 10 (Nov 2025) | Remove external dependency |
| EF Core without AUTOINCREMENT control on SQLite | EF Core 10 allows disabling AUTOINCREMENT | EF Core 10 (Nov 2025) | Slightly better SQLite performance for event IDs |
| Named query filters workaround | Named Query Filters in EF Core 10 | EF Core 10 (Nov 2025) | Cleaner soft-delete or tenant filtering |

**Deprecated/outdated:**
- The scaffold uses `TypedResults.ServerSentEvents(stream, eventType: "sessionEvent")` with a raw `IAsyncEnumerable<SessionEvent>` -- this works but loses event ID support. Must wrap in `SseItem<SessionEvent>` for replay.

## Open Questions

1. **Event storage: all-in-DB vs hybrid?**
   - What we know: SQLite handles typical session sizes (hundreds to low thousands of events) fine. Large sessions with heavy stdout streaming could produce tens of thousands of events.
   - What's unclear: At what event volume SQLite becomes a bottleneck for a single session's history.
   - Recommendation: Start with all-in-DB. Add a configurable max event retention per session (e.g., 10,000). If sessions exceed this, older events can be archived or truncated. This is simpler to implement and sufficient for Phase 1.

2. **Host daemon protocol definition**
   - What we know: Success criteria says "Host daemon concept is defined with a receivable command protocol (even if initially stub/SSH-based)." Phase 2 will implement actual SSH execution.
   - What's unclear: Exact wire protocol format (REST? gRPC? raw SSH commands?).
   - Recommendation: Define a simple JSON-over-SSH command protocol in Phase 1. The host daemon receives JSON commands (start-session, stop-session, report-status) over SSH stdin. This is the simplest thing that works and aligns with the SSH backend decision.

3. **IAgentAdapter vs ISessionBackend boundary**
   - What we know: Currently ISessionBackend conflates "where to run" with "what to run." The user's context leaves this as Claude's discretion.
   - Recommendation: Introduce IAgentAdapter as a separate concern. ISessionBackend handles transport/placement (SSH to host, local execution). IAgentAdapter handles agent invocation (build CLI command, parse output). The coordinator composes them: pick a backend, pick an adapter, run the adapter on the backend. This makes Phase 2 cleaner because adding new agents doesn't require new backends and vice versa.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit + Microsoft.AspNetCore.Mvc.Testing (for integration tests) |
| Config file | None -- needs creation in Wave 0 |
| Quick run command | `dotnet test tests/AgentHub.Tests/ --filter "Category!=Integration"` |
| Full suite command | `dotnet test tests/AgentHub.Tests/` |

### Phase Requirements -> Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| INFRA-01 | Health check and REST endpoints respond | integration | `dotnet test tests/AgentHub.Tests/ --filter "FullyQualifiedName~ApiEndpoints" -x` | No -- Wave 0 |
| INFRA-02 | Host daemon command protocol defined | unit | `dotnet test tests/AgentHub.Tests/ --filter "FullyQualifiedName~HostDaemon" -x` | No -- Wave 0 |
| INFRA-03 | Session data persists across restarts | integration | `dotnet test tests/AgentHub.Tests/ --filter "FullyQualifiedName~Persistence" -x` | No -- Wave 0 |
| MON-01 | SSE streams events with replay support | integration | `dotnet test tests/AgentHub.Tests/ --filter "FullyQualifiedName~SseStreaming" -x` | No -- Wave 0 |
| AGENT-01 | Agent adapter interface with Claude Code impl | unit | `dotnet test tests/AgentHub.Tests/ --filter "FullyQualifiedName~AgentAdapter" -x` | No -- Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test tests/AgentHub.Tests/ --filter "Category!=Integration" -x`
- **Per wave merge:** `dotnet test tests/AgentHub.Tests/`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `tests/AgentHub.Tests/AgentHub.Tests.csproj` -- new test project with xUnit, Microsoft.AspNetCore.Mvc.Testing, Microsoft.EntityFrameworkCore.InMemory
- [ ] `tests/AgentHub.Tests/ApiEndpointTests.cs` -- WebApplicationFactory-based integration tests for INFRA-01
- [ ] `tests/AgentHub.Tests/PersistenceTests.cs` -- EF Core in-memory provider tests for INFRA-03
- [ ] `tests/AgentHub.Tests/SseStreamingTests.cs` -- SSE endpoint tests with Last-Event-ID for MON-01
- [ ] `tests/AgentHub.Tests/AgentAdapterTests.cs` -- Claude Code adapter unit tests for AGENT-01
- [ ] `tests/AgentHub.Tests/HostDaemonProtocolTests.cs` -- protocol definition tests for INFRA-02
- [ ] Solution file updated to include test project
- [ ] Framework install: `dotnet add tests/AgentHub.Tests/ package xunit --version 2.9.* && dotnet add tests/AgentHub.Tests/ package Microsoft.AspNetCore.Mvc.Testing`

## Sources

### Primary (HIGH confidence)
- [.NET 10 SDK 10.0.103] -- installed and verified locally
- [Milan Jovanovic - SSE in ASP.NET Core .NET 10](https://www.milanjovanovic.tech/blog/server-sent-events-in-aspnetcore-and-dotnet-10) -- TypedResults.ServerSentEvents, SseItem<T>, Last-Event-ID replay pattern
- [Microsoft Learn - TypedResults.ServerSentEvents](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.typedresults.serversentevents?view=aspnetcore-10.0) -- Official API reference
- [NuGet - CliWrap 3.10.0](https://www.nuget.org/packages/CliWrap) -- Latest version confirmed November 2025
- [NuGet - EF Core SQLite 10.0.2](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Sqlite/) -- Latest version confirmed
- [Claude Code CLI Reference](https://code.claude.com/docs/en/cli-reference) -- Complete flag reference including --output-format, --dangerously-skip-permissions, --allowedTools

### Secondary (MEDIUM confidence)
- [Microsoft Learn - EF Core 10 What's New](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew) -- Named query filters, AUTOINCREMENT control
- [GitHub - Tyrrrz/CliWrap](https://github.com/Tyrrrz/CliWrap) -- CliWrap API patterns and examples

### Tertiary (LOW confidence)
- Claude Code stream-json output schema -- not formally documented; the exact NDJSON event structure may change between versions. Adapter should parse defensively.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- all libraries are official Microsoft or well-established NuGet packages with recent releases
- Architecture: HIGH -- patterns are standard ASP.NET Core + EF Core; SSE pattern verified against .NET 10 docs
- Pitfalls: HIGH -- SQLite concurrency, SSE lifecycle, and CLI instability are well-documented concerns
- Agent adapter: MEDIUM -- the Claude Code stream-json output format is not formally versioned; adapter will need defensive parsing

**Research date:** 2026-03-08
**Valid until:** 2026-04-08 (30 days -- stable ecosystem, .NET 10 LTS patterns)
