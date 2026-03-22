# Phase 10: Multi-Agent Coordination - Research

**Researched:** 2026-03-22
**Domain:** Multi-agent session orchestration, parent-child tracking, weighted placement, SSE event routing
**Confidence:** HIGH

## Summary

Phase 10 adds multi-agent coordination to AgentHub: the ability to spawn child sessions from parent sessions, track parent-child relationships, route child events to parent SSE streams, upgrade the placement engine to weighted scoring, and enforce cascade safety limits. The codebase is well-prepared for this phase -- `SessionEntity.ParentSessionId` (nullable FK with SetNull delete behavior) and `DispatchId` columns already exist in the schema from Phase 7 migrations, `HostMetricCache` provides real-time CPU/memory snapshots, and `SimplePlacementEngine` has clear extension points.

The primary implementation challenge is the event routing: DurableEventService currently broadcasts events to session-specific and fleet-wide channels only. Adding parent-stream forwarding requires looking up the parent session ID for each child event and writing to the parent's channel. The SSH stdout intercept for spawn commands is a secondary mechanism that hooks into the existing `StartStreamingCommandAsync` PTY reader. The weighted placement scoring is pure computation over data already available in `HostMetricCache`.

**Primary recommendation:** Implement in three waves -- (1) spawn API + parent-child DB tracking + placement scoring, (2) child-to-parent event routing + SSE stream changes, (3) dashboard/CLI tree visualization + safety limits enforcement.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- Both operators (CLI/Web UI) and running agents can spawn child sessions
- Two spawn mechanisms: Coordinator HTTP API (POST /api/sessions with ParentSessionId) and SSH stdout intercept
- Child sessions inherit parent's agent type, accept-risk, and worktree mode by default; spawn request can override
- Child events appear inline on parent's SSE stream with [child-id] prefix
- Default: parent stream gets lifecycle events only (ChildSpawned, ChildCompleted, ChildFailed)
- Opt-in full child output via query param (?includeChildren=true)
- When parent session stops, children continue running independently (orphan behavior)
- Parent stop emits a warning that children are still active
- Web: Tree view with expandable children indented underneath; children also accessible standalone
- CLI: Indented tree using Spectre.Console Tree component
- Placement scoring: weighted CPU%, free memory, active session count
- Disk space as hard filter (reject below threshold), not scoring factor
- Weights configurable in appsettings.json with sensible defaults
- TargetHostId override bypasses scoring (existing behavior preserved)
- Stale metrics (older than 2x polling interval) get penalty score; no metrics = excluded unless targeted
- Max nesting depth: 3 (configurable)
- Max children per parent: configurable, derived from host capabilities
- Per-host concurrent session limit (configurable)
- Exceed limits: reject with HTTP 400/429 and clear error message

### Claude's Discretion
- SSH stdout spawn command format and intercept pattern
- Exact placement scoring formula and normalization approach
- ChildSpawned/ChildCompleted/ChildFailed event payload design
- Per-host session limit storage (appsettings vs host config vs DB column)
- Tree view component implementation details in MudBlazor
- Spawn request DTO design (extend StartSessionRequest vs new SpawnRequest)
- Stale metric penalty magnitude and threshold calculation

### Deferred Ideas (OUT OF SCOPE)
- MCP tool as third spawn mechanism -- future MCP protocol phase
- Batch API for launching N sessions in one call (COORD-10) -- v1.2
- Session dependency DAG with fan-out/fan-in execution (COORD-11) -- v1.2
- Token/cost budget inheritance from parent to child (COORD-12) -- v1.2
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| COORD-01 | Running session can spawn child sessions on other hosts via coordinator API | Extend StartSessionRequest with ParentSessionId; SessionCoordinator wires FK; SSH stdout intercept as second spawn path |
| COORD-02 | Parent-child session relationships are tracked in the database | SessionEntity.ParentSessionId FK already exists; add ChildSessions navigation; extend SessionSummary DTO |
| COORD-03 | Child session events are visible on parent's SSE stream | DurableEventService.EmitAsync forwards child events to parent channel; new event kinds for lifecycle |
| COORD-04 | Placement engine uses weighted scoring of CPU, memory, and session count | Replace SimplePlacementEngine.ChooseNode FirstOrDefault with scored ranking using HostMetricCache data |
| COORD-05 | Sub-agent spawning enforces depth and count limits to prevent cascades | Depth check via recursive ParentSessionId walk; count check via DB query; per-host limit via DB count |
</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| ASP.NET Core Minimal API | .NET 10 | Spawn endpoint, parent-child query endpoints | Already used for all API endpoints |
| EF Core + SQLite | 10.0.2 | Parent-child FK queries, session count queries | Existing persistence layer |
| SSH.NET | (existing) | PTY ShellStream for stdout spawn intercept | Already used for all SSH operations |
| System.Threading.Channels | (built-in) | SSE event routing for child-to-parent forwarding | Already used by SseSubscriptionManager |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Spectre.Console | 0.50.0 | CLI tree view for parent-child session hierarchy | CLI session list command |
| MudBlazor | v9 | Web UI tree view / hierarchical data grid | Dashboard session visualization |
| IOptions<T> | (built-in) | Placement weights, cascade limits configuration | appsettings.json binding |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Extend StartSessionRequest | New SpawnRequest DTO | Extending existing DTO is simpler; SpawnRequest adds clarity but duplicates fields. Recommend extending StartSessionRequest with optional ParentSessionId |
| Store per-host session limit in DB | appsettings.json | DB allows per-host customization; appsettings is simpler. Recommend appsettings with global default + optional per-host override on HostEntity |

## Architecture Patterns

### Recommended Project Structure
```
src/AgentHub.Contracts/
    Models.cs                    # Add ParentSessionId to StartSessionRequest, SessionSummary; new event kinds
src/AgentHub.Orchestration/
    Placement/
        SimplePlacementEngine.cs # Upgrade to weighted scoring
        PlacementOptions.cs      # NEW: IOptions<T> for weights + limits
    Coordinator/
        SessionCoordinator.cs    # Wire ParentSessionId, depth/count validation
        SpawnInterceptor.cs      # NEW: SSH stdout spawn command parser
    Events/
        DurableEventService.cs   # Child-to-parent event forwarding
        SseSubscriptionManager.cs # includeChildren support
    Monitoring/
        HostMetricCache.cs       # Add active session count tracking
    Config/
        CoordinationOptions.cs   # NEW: IOptions<T> for cascade limits
src/AgentHub.Service/
    Program.cs                   # New spawn endpoint, parent-child query params
src/AgentHub.Cli/
    Commands/Session/            # Tree view in session list
src/AgentHub.Web/
    Components/Shared/
        SessionTable.razor       # Add parent-child tree indentation
```

### Pattern 1: Spawn via Coordinator HTTP API
**What:** Agents or operators POST to /api/sessions with ParentSessionId field. SessionCoordinator validates depth/count limits, then delegates to existing StartSessionAsync flow with parent FK wired.
**When to use:** Primary spawn mechanism for all API-driven spawning.
**Example:**
```csharp
// In SessionCoordinator.StartSessionAsync - add parent-child wiring
public async Task<string> StartSessionAsync(string userId, StartSessionRequest request,
    Func<SessionEvent, Task> emit, CancellationToken ct)
{
    // Validate cascade limits if this is a child spawn
    if (!string.IsNullOrEmpty(request.ParentSessionId))
    {
        await ValidateCascadeLimitsAsync(request.ParentSessionId, ct);
    }

    // ... existing placement + backend dispatch ...

    // After session entity creation, set ParentSessionId
    entity.ParentSessionId = request.ParentSessionId;

    // Emit ChildSpawned event to parent's stream
    if (!string.IsNullOrEmpty(request.ParentSessionId))
    {
        await emit(new SessionEvent(request.ParentSessionId,
            SessionEventKind.ChildSpawned, DateTimeOffset.UtcNow,
            $"Child session {sessionId} spawned on {placement.NodeId}",
            new Dictionary<string, string> { ["childSessionId"] = sessionId }));
    }
}
```

### Pattern 2: SSH Stdout Spawn Intercept
**What:** Agent writes a spawn marker to stdout (e.g., `##AGENTHUB_SPAWN:{"agent":"claude","prompt":"..."}##`). The PTY reader in `ReadAgentOutputAsync` detects this pattern, parses the JSON payload, and calls SessionCoordinator.StartSessionAsync with the current session as parent.
**When to use:** Agents that can write to stdout but cannot make HTTP calls.
**Example:**
```csharp
// In SshBackend.ReadAgentOutputAsync - add spawn intercept before emitting StdOut
private static readonly Regex SpawnPattern =
    new(@"##AGENTHUB_SPAWN:(\{.*\})##", RegexOptions.Compiled);

// Inside the line processing loop:
var match = SpawnPattern.Match(clean);
if (match.Success)
{
    var spawnJson = match.Groups[1].Value;
    var spawnReq = JsonSerializer.Deserialize<SpawnRequest>(spawnJson);
    // Spawn child via coordinator (fire-and-forget, don't block PTY reader)
    _ = Task.Run(() => SpawnChildAsync(sessionId, spawnReq, emit));
    continue; // Don't emit spawn marker as StdOut
}
```

### Pattern 3: Weighted Placement Scoring
**What:** Replace `FirstOrDefault()` with a scored ranking. Each eligible node gets a composite score from normalized CPU availability, memory availability, and inverse session count. Configurable weights default to 0.4/0.3/0.3.
**When to use:** Every placement decision (except when TargetHostId is explicitly set).
**Example:**
```csharp
// In SimplePlacementEngine.ChooseNode - replace FirstOrDefault with scoring
var candidates = q.ToList();
if (candidates.Count == 0)
    throw new InvalidOperationException("No eligible node found.");

if (!string.IsNullOrWhiteSpace(req.TargetHostId))
    return new PlacementDecision(candidates[0].Backend, candidates[0].NodeId);

var scored = candidates
    .Select(n => new { Node = n, Score = ScoreNode(n, metricCache, options) })
    .Where(x => x.Score >= 0) // Negative = disqualified (stale/disk)
    .OrderByDescending(x => x.Score)
    .FirstOrDefault()
    ?? throw new InvalidOperationException("No eligible node found.");

return new PlacementDecision(scored.Node.Backend, scored.Node.NodeId);
```

### Pattern 4: Child-to-Parent Event Forwarding
**What:** In DurableEventService.EmitAsync, after broadcasting to the child's session channel and fleet, also look up the child's ParentSessionId and forward lifecycle events (or all events if includeChildren) to the parent's session channel.
**When to use:** Every event emitted by a child session.
**Example:**
```csharp
// In DurableEventService.EmitAsync - add parent forwarding
if (!string.IsNullOrEmpty(ev.SessionId))
{
    var parentId = await GetParentSessionIdAsync(ev.SessionId);
    if (parentId is not null)
    {
        var isLifecycleEvent = ev.Kind is SessionEventKind.ChildSpawned
            or SessionEventKind.ChildCompleted or SessionEventKind.ChildFailed
            or SessionEventKind.StateChanged;

        // Forward lifecycle events always; other events only if includeChildren
        var prefixedEvent = ev with { Data = $"[{ev.SessionId}] {ev.Data}" };
        var parentSseItem = new SseItem<SessionEvent>(prefixedEvent, "sessionEvent")
        {
            EventId = eventId
        };

        _subs.BroadcastToParent(parentId, parentSseItem, lifecycleOnly: isLifecycleEvent);
    }
}
```

### Anti-Patterns to Avoid
- **Recursive parent lookup on every event:** Cache the parent-child mapping in memory (ConcurrentDictionary keyed by child session ID) instead of querying DB on every event emission.
- **Blocking PTY reader for spawn:** The SSH stdout spawn intercept must fire-and-forget the child spawn to avoid blocking the parent's output stream. Use `Task.Run` without await.
- **Deep recursion for depth check:** Walk the ParentSessionId chain iteratively, not recursively. With max depth 3, this is at most 3 DB reads, but iterative is clearer.
- **Global session count for placement:** Count sessions per-host (WHERE Node = hostId AND State = Running), not globally. The per-host count matters for resource contention.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Tree rendering (CLI) | Custom ASCII tree drawing | `Spectre.Console.Tree` | Already in dependencies; handles Unicode box-drawing, colors, nesting |
| Tree rendering (Web) | Custom recursive Blazor components | MudBlazor `MudTreeView` or nested `MudDataGrid` with grouping | Handles expand/collapse, accessibility, consistent styling |
| Configuration binding | Manual IConfiguration reads | `IOptions<PlacementOptions>` + `IOptions<CoordinationOptions>` | Type-safe, validated, hot-reload capable |
| Parent-child ID caching | Manual invalidation logic | ConcurrentDictionary with lazy population from DB | Simple, thread-safe, clears on session completion |

## Common Pitfalls

### Pitfall 1: Event Forwarding Infinite Loops
**What goes wrong:** If the forwarded parent event is itself treated as a child event and re-forwarded, you get infinite event loops.
**Why it happens:** The forwarding logic does not distinguish between original events and forwarded events.
**How to avoid:** Use a meta tag (e.g., `["forwarded"] = "true"`) on forwarded events and skip forwarding for events already marked as forwarded.
**Warning signs:** Exponential event count growth, SSE stream flooding.

### Pitfall 2: Stale Parent Session ID Cache
**What goes wrong:** If a child session's parent is updated or the parent session is deleted (SetNull FK), the cached parent mapping becomes stale.
**Why it happens:** In-memory cache not invalidated on parent session state changes.
**How to avoid:** Invalidate cache entry when parent session stops/fails. The SetNull FK behavior means ParentSessionId becomes null on parent delete -- cache should handle this.
**Warning signs:** Events forwarded to nonexistent parent channels (harmless but wasteful).

### Pitfall 3: Race Condition on Depth Check
**What goes wrong:** Two concurrent spawn requests from the same parent both pass the depth check before either is persisted, exceeding the depth limit.
**Why it happens:** Check-then-act pattern without locking.
**How to avoid:** Use a per-parent semaphore or optimistic concurrency. With SQLite's serialized writes and max depth 3, the window is small. Accept the minor race or add a SemaphoreSlim keyed by parent session ID.
**Warning signs:** Depth exceeds configured maximum by 1.

### Pitfall 4: Session Count Query Performance
**What goes wrong:** Counting active sessions per host on every placement decision hits the DB.
**Why it happens:** No in-memory tracking of session-to-host mapping.
**How to avoid:** Maintain a ConcurrentDictionary<hostId, int> counter in a singleton service, incremented on session start, decremented on session stop/fail. The counter is the source of truth for placement; DB is backup.
**Warning signs:** Slow placement decisions under load.

### Pitfall 5: SQLite DateTimeOffset Ordering
**What goes wrong:** ORDER BY on DateTimeOffset columns in SQLite produces wrong results.
**Why it happens:** Known SQLite limitation with EF Core DateTimeOffset storage.
**How to avoid:** Follow existing pattern: fetch to memory with ToListAsync(), then sort with LINQ. Already used in SessionCoordinator.GetSessionHistoryAsync.
**Warning signs:** Sessions appearing in wrong order in tree views.

## Code Examples

### Cascade Depth Validation
```csharp
// Source: derived from existing SessionEntity.ParentSessionId FK
private async Task<int> GetSessionDepthAsync(string sessionId, CancellationToken ct)
{
    await using var db = await _dbFactory.CreateDbContextAsync(ct);
    int depth = 0;
    string? currentId = sessionId;

    while (currentId is not null && depth <= _options.MaxDepth)
    {
        var session = await db.Sessions
            .Where(s => s.SessionId == currentId)
            .Select(s => s.ParentSessionId)
            .FirstOrDefaultAsync(ct);
        currentId = session;
        if (currentId is not null) depth++;
    }

    return depth;
}

private async Task ValidateCascadeLimitsAsync(string parentSessionId, CancellationToken ct)
{
    // Depth check
    var parentDepth = await GetSessionDepthAsync(parentSessionId, ct);
    if (parentDepth >= _options.MaxDepth)
        throw new InvalidOperationException(
            $"Maximum nesting depth ({_options.MaxDepth}) exceeded. " +
            $"Parent session is at depth {parentDepth}.");

    // Children count check
    await using var db = await _dbFactory.CreateDbContextAsync(ct);
    var childCount = await db.Sessions
        .CountAsync(s => s.ParentSessionId == parentSessionId
                      && s.State == SessionState.Running, ct);
    if (childCount >= _options.MaxChildrenPerParent)
        throw new InvalidOperationException(
            $"Maximum children per parent ({_options.MaxChildrenPerParent}) exceeded. " +
            $"Current active children: {childCount}.");
}
```

### Weighted Placement Scoring
```csharp
// Source: derived from existing HostMetricCache + SimplePlacementEngine
public sealed class PlacementOptions
{
    public double CpuWeight { get; set; } = 0.4;
    public double MemoryWeight { get; set; } = 0.3;
    public double SessionWeight { get; set; } = 0.3;
    public double MinDiskFreeGb { get; set; } = 5.0;
    public double StaleMetricPenalty { get; set; } = 0.5; // Multiplier (0.5 = halve the score)
    public int MaxSessionsPerHost { get; set; } = 5;
}

private double ScoreNode(NodeCapability node, HostMetricCache cache, PlacementOptions opts)
{
    var snapshot = cache.Get(node.NodeId);
    if (snapshot is null) return -1; // No metrics = excluded

    // Stale check: metrics older than 2x polling interval (60s)
    var metricsAge = DateTimeOffset.UtcNow - (snapshot.MetricsUpdatedUtc ?? DateTimeOffset.MinValue);
    if (metricsAge > TimeSpan.FromSeconds(60))
        return -1; // Too stale = excluded

    // Hard filter: disk space
    if (snapshot.Inventory?.DiskFreeGb < opts.MinDiskFreeGb)
        return -1;

    // CPU score: higher available CPU = better (0-1)
    double cpuScore = snapshot.CpuPercent.HasValue
        ? Math.Max(0, (100.0 - snapshot.CpuPercent.Value) / 100.0)
        : 0.5; // Unknown = neutral

    // Memory score: higher free memory = better (0-1)
    double memScore = (snapshot.MemTotalMb > 0 && snapshot.MemUsedMb.HasValue)
        ? (double)(snapshot.MemTotalMb!.Value - snapshot.MemUsedMb.Value) / snapshot.MemTotalMb.Value
        : 0.5;

    // Session count score: fewer sessions = better (0-1)
    int activeSessions = GetActiveSessionCount(node.NodeId);
    if (activeSessions >= opts.MaxSessionsPerHost)
        return -1; // At capacity
    double sessionScore = 1.0 - ((double)activeSessions / opts.MaxSessionsPerHost);

    double score = (cpuScore * opts.CpuWeight)
                 + (memScore * opts.MemoryWeight)
                 + (sessionScore * opts.SessionWeight);

    // Apply stale penalty if metrics are somewhat old (but not excluded)
    if (metricsAge > TimeSpan.FromSeconds(30))
        score *= opts.StaleMetricPenalty;

    return score;
}
```

### New SessionEventKind Additions
```csharp
// Add to existing SessionEventKind enum
public enum SessionEventKind
{
    // ... existing values ...
    SteeringInput,
    SteeringDelivered,
    // Phase 10 additions (append to end for integer stability):
    ChildSpawned,
    ChildCompleted,
    ChildFailed
}
```

### SSH Stdout Spawn Intercept
```csharp
// Spawn marker format: single-line JSON wrapped in markers
// Agent writes: ##AGENTHUB_SPAWN:{"agent":"claude","prompt":"implement auth"}##
// Minimal required fields; optional fields inherit from parent

public sealed record SpawnCommand(
    string Agent,
    string Prompt,
    string? TargetHostId = null,
    bool? AcceptRisk = null,
    bool? Worktree = null);

// Detection in ReadAgentOutputAsync:
private static readonly Regex SpawnMarker =
    new(@"##AGENTHUB_SPAWN:(\{[^#]+\})##", RegexOptions.Compiled);
```

### Spectre.Console Tree for CLI
```csharp
// Source: Spectre.Console 0.50.0 Tree API
var tree = new Tree("[bold]Sessions[/]");

foreach (var parent in sessions.Where(s => s.ParentSessionId is null))
{
    var node = tree.AddNode($"[green]{Truncate(parent.SessionId, 8)}[/] {parent.State} @ {parent.Node}");
    var children = sessions.Where(s => s.ParentSessionId == parent.SessionId);
    foreach (var child in children)
    {
        node.AddNode($"[dim]{Truncate(child.SessionId, 8)}[/] {child.State} @ {child.Node}");
    }
}

AnsiConsole.Write(tree);
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| SimplePlacementEngine.FirstOrDefault() | Weighted scoring with HostMetricCache | Phase 10 | Optimal host selection instead of arbitrary first match |
| Session events only on own stream | Child events forwarded to parent stream | Phase 10 | Parent sessions see full picture of sub-agent work |
| Flat session list | Hierarchical parent-child tree | Phase 10 | Visual clarity for multi-agent workflows |

## Open Questions

1. **Per-host session limit storage**
   - What we know: appsettings.json is simplest; HostEntity could gain a MaxSessions column
   - What's unclear: Whether different hosts need different limits
   - Recommendation: Start with appsettings.json global default (`PlacementOptions.MaxSessionsPerHost`). If per-host customization is needed later, add a nullable column to HostEntity that overrides the global default.

2. **Active session count source of truth**
   - What we know: DB query (WHERE State = Running AND Node = hostId) is authoritative but slow per-placement; HostDaemon's `HostStatusReport.ActiveSessions` exists but may count non-AgentHub processes
   - What's unclear: Whether the DB count or in-memory counter is more reliable
   - Recommendation: Use a singleton `ActiveSessionTracker` (ConcurrentDictionary<string, int>) incremented/decremented by SessionCoordinator. Initialize from DB on startup. This avoids per-placement DB queries.

3. **includeChildren query param implementation**
   - What we know: SseSubscriptionManager needs to distinguish "lifecycle only" vs "all events" forwarding per parent subscriber
   - What's unclear: Whether this is per-subscription or per-request
   - Recommendation: Per-subscription (set at SSE connect time via query param). Store a flag alongside the ChannelWriter in the subscriber dictionary.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xunit 2.9.x on .NET 10 |
| Config file | tests/AgentHub.Tests/AgentHub.Tests.csproj |
| Quick run command | `dotnet test tests/AgentHub.Tests --filter "Category=Phase10" -x` |
| Full suite command | `dotnet test tests/AgentHub.Tests` |

### Phase Requirements to Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| COORD-01 | Spawn child session via API with ParentSessionId | integration | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SpawnSession" -x` | No - Wave 0 |
| COORD-02 | Parent-child relationship persisted and queryable | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~ParentChild" -x` | No - Wave 0 |
| COORD-03 | Child events forwarded to parent SSE stream | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~ChildEventForwarding" -x` | No - Wave 0 |
| COORD-04 | Weighted placement scoring selects best host | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~PlacementScoring" -x` | No - Wave 0 |
| COORD-05 | Depth/count limits enforced on spawn | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~CascadeLimit" -x` | No - Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test tests/AgentHub.Tests --filter "Category=Phase10" -x`
- **Per wave merge:** `dotnet test tests/AgentHub.Tests`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `tests/AgentHub.Tests/PlacementScoringTests.cs` -- covers COORD-04 weighted scoring
- [ ] `tests/AgentHub.Tests/CascadeLimitTests.cs` -- covers COORD-05 depth/count validation
- [ ] `tests/AgentHub.Tests/ChildEventForwardingTests.cs` -- covers COORD-03 parent stream routing
- [ ] `tests/AgentHub.Tests/SpawnSessionTests.cs` -- covers COORD-01, COORD-02 spawn + persistence

## Sources

### Primary (HIGH confidence)
- Codebase analysis of all files in `src/AgentHub.Orchestration/`, `src/AgentHub.Contracts/`, `src/AgentHub.Service/Program.cs`
- SessionEntity.cs: ParentSessionId FK confirmed in schema + DbContext OnModelCreating
- SimplePlacementEngine.cs: Current FirstOrDefault implementation confirmed
- DurableEventService.cs: Current emit + broadcast flow confirmed
- SseSubscriptionManager.cs: Per-session and fleet channel architecture confirmed
- HostMetricCache.cs: ConcurrentDictionary with CPU/memory/inventory confirmed
- SshHostConnection.cs: StartStreamingCommandAsync PTY reader loop confirmed

### Secondary (MEDIUM confidence)
- Spectre.Console 0.50.0 Tree API -- based on project dependency and Spectre.Console documentation
- MudBlazor v9 MudTreeView -- based on MudBlazor v9 component library (project already uses MudBlazor v9)

### Tertiary (LOW confidence)
- None

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - all libraries already in use; no new dependencies needed
- Architecture: HIGH - patterns derive directly from existing codebase with clear extension points
- Pitfalls: HIGH - identified from direct code analysis of event routing and placement logic

**Research date:** 2026-03-22
**Valid until:** 2026-04-22 (stable internal codebase)
