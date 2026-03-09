# Architecture: v1.1 Integration Patterns

**Domain:** Multi-agent orchestration platform -- extending v1.0 with interactive sessions, multi-agent coordination, host inventory, and git worktree isolation
**Researched:** 2026-03-09
**Confidence:** HIGH (based on direct codebase analysis of 12,000 LOC shipped v1.0)

## Existing Architecture Snapshot

Before defining v1.1 integration, here is the relevant v1.0 surface area each feature touches.

### Current Component Map

```
Coordinator API (Program.cs)
  POST /api/sessions           -> SessionCoordinator.StartSessionAsync()
  POST /api/sessions/{id}/input -> SessionCoordinator.SendInputAsync()
  DELETE /api/sessions/{id}    -> SessionCoordinator.StopSessionAsync()
  GET  /api/sessions/{id}/events -> DurableEventService.SubscribeSession() [SSE]
  GET  /api/events             -> DurableEventService.SubscribeFleet() [SSE]
  GET  /api/hosts              -> IHostRegistry.ListAsync()

SessionCoordinator (singleton, DI-injected)
  -> IEnumerable<ISessionBackend> (InMemory, SSH)
  -> IPlacementEngine (SimplePlacementEngine)
  -> ISanitizationService, ISkillPolicyService, ApprovalService

SshBackend (singleton)
  -> ConcurrentDictionary<string, ISshHostConnection> _connections
  -> IHostRegistry, ISshHostConnectionFactory, IDbContextFactory
  -> HostCommandProtocol (JSON-over-SSH-stdin/stdout)

DurableEventService (singleton)
  -> IDbContextFactory<AgentHubDbContext>
  -> SseSubscriptionManager (Channel<T> per subscriber)

Data Model:
  SessionEntity { SessionId, State, Backend, Node, WorktreePath, Prompt, ... }
  SessionEventEntity { Id, SessionId, Kind, TsUtc, Data, Meta }
  HostEntity, ApprovalEntity
```

### Critical Existing Abstractions

| Interface | Methods That Matter for v1.1 |
|-----------|------------------------------|
| `ISessionBackend` | `StartAsync(emit)`, `SendInputAsync()`, `StopAsync()`, `GetInventoryAsync()` |
| `ISessionCoordinator` | `StartSessionAsync(emit)`, `SendInputAsync(emit)` |
| `IPlacementEngine` | `ChooseNode(requirements, inventory)` |
| `IHostRegistry` | `ListAsync()`, `GetAsync(hostId)` |
| `IWorktreeProvider` | `EnsureMaterializedAsync(descriptor, root)` |

---

## Feature 1: Interactive Session Steering

### What Changes

Interactive sessions add mid-run control: pause, resume, redirect, and send follow-up instructions to running agents. v1.0 has `SendInputAsync` which pipes text to the agent's stdin -- this is the foundation but it lacks session-state semantics (pause/resume) and structured control signals.

### Integration Points

**MODIFY: `SessionState` enum** (Contracts/Models.cs)

Add `Paused = 4` state. Currently: `Pending(0), Running(1), Stopped(2), Failed(3)`.

```csharp
public enum SessionState
{
    Pending = 0, Running = 1, Stopped = 2, Failed = 3,
    Paused = 4  // NEW: agent process suspended, resumable
}
```

**MODIFY: `SendInputRequest`** (Contracts/Models.cs)

Add a `ControlSignal` field to distinguish steering commands from raw text input:

```csharp
public sealed record SendInputRequest(
    string Input,
    bool IsBinary = false,
    string? SkillId = null,
    Dictionary<string, string>? Arguments = null,
    bool RequiresElevation = false,
    SessionControlSignal? Signal = null);  // NEW

public enum SessionControlSignal
{
    None = 0,
    Pause = 1,
    Resume = 2,
    Redirect = 3  // Input field contains new direction
}
```

**MODIFY: `SessionCoordinator.SendInputAsync()`** (Coordinator/SessionCoordinator.cs)

Before dispatching to backend, handle control signals:
- `Pause`: Update session state to `Paused` in DB, send SIGTSTP-equivalent to backend
- `Resume`: Verify state is `Paused`, update to `Running`, send SIGCONT-equivalent
- `Redirect`: Send the new prompt as input (existing path) but emit a `Redirect` event for audit
- Policy/sanitization still runs on all inputs including redirects

**MODIFY: `HostCommandProtocol`** (HostDaemon/HostCommandProtocol.cs)

Add new commands:
- `pause-session` -- tells host daemon to suspend the agent process (SIGTSTP on Linux, not available on all agents)
- `resume-session` -- SIGCONT to resume

**MODIFY: `SshBackend.SendInputAsync()`** (Backends/SshBackend.cs)

Route control signals to the appropriate HostCommand rather than piping as stdin text.

**NEW: `SessionEventKind.Redirect`** (Contracts/Models.cs)

Add to the enum for audit trail when direction changes mid-session.

**NO CHANGE: DurableEventService, SseSubscriptionManager**

The event infrastructure already supports arbitrary event kinds. Interactive events flow through the existing `emit` callback pipeline.

### Data Flow

```
Client: POST /api/sessions/{id}/input { signal: "Pause" }
  -> SessionCoordinator.SendInputAsync()
     -> if signal == Pause:
        1. Update SessionEntity.State = Paused (DB)
        2. backend.SendControlAsync(sessionId, "pause-session")
        3. emit(SessionEvent { Kind: StateChanged, Data: "Paused" })
     -> if signal == Resume:
        1. Verify state == Paused
        2. Update SessionEntity.State = Running
        3. backend.SendControlAsync(sessionId, "resume-session")
        4. emit(SessionEvent { Kind: StateChanged, Data: "Running" })
     -> if signal == Redirect:
        1. emit(SessionEvent { Kind: Redirect, Data: newPrompt })
        2. backend.SendInputAsync(sessionId, newPrompt)  // existing path
```

### Key Design Decision

Do NOT add a separate `/api/sessions/{id}/pause` endpoint. Routing all interactions through the existing `/input` endpoint with a `Signal` discriminator keeps the API surface small and ensures policy/sanitization runs on every interaction uniformly. The `SendInputRequest` already goes through the full coordinator pipeline.

---

## Feature 2: Multi-Agent Coordination

### What Changes

Enable dispatching tasks across machines, spawning sub-agents from a parent session, and resource-aware scheduling. This is the most architecturally significant v1.1 feature.

### Integration Points

**NEW: `ICoordinationService`** (Orchestration/Coordination/)

New service that sits alongside `SessionCoordinator`. Responsibilities:
- Dispatch: create multiple sessions from a single request with task decomposition
- Sub-agent spawning: allow a running session to request child sessions
- Tracking parent-child relationships between sessions

```csharp
public interface ICoordinationService
{
    Task<string[]> DispatchAsync(string userId, DispatchRequest request,
        Func<SessionEvent, Task> emit, CancellationToken ct);
    Task<string> SpawnSubAgentAsync(string parentSessionId, StartSessionRequest request,
        Func<SessionEvent, Task> emit, CancellationToken ct);
    Task<IReadOnlyList<SessionSummary>> GetChildSessionsAsync(string parentSessionId,
        CancellationToken ct);
}
```

**NEW: `DispatchRequest`** (Contracts/Models.cs)

```csharp
public sealed record DispatchRequest(
    DispatchTask[] Tasks,
    DispatchStrategy Strategy = DispatchStrategy.Parallel);

public sealed record DispatchTask(
    string TaskId,
    StartSessionRequest Request,
    string[]? DependsOn = null);

public enum DispatchStrategy { Parallel, Sequential, DependencyGraph }
```

**MODIFY: `SessionEntity`** (Data/Entities/SessionEntity.cs)

Add parent-child tracking:

```csharp
public string? ParentSessionId { get; set; }  // NEW: null for root sessions
public string? DispatchId { get; set; }         // NEW: groups related dispatched sessions
```

**MODIFY: `SessionSummary`** (Contracts/Models.cs)

Expose parent/dispatch info to clients:

```csharp
public sealed record SessionSummary(
    // ... existing fields ...
    string? ParentSessionId = null,   // NEW
    string? DispatchId = null          // NEW
);
```

**MODIFY: `SimplePlacementEngine` -> `ResourceAwarePlacementEngine`** (Placement/)

The current engine does filter-only placement (first match wins). Replace with a scoring engine that considers:
1. Resource availability: CPU/memory headroom from `HostMetricPollingService` data
2. Session count: prefer hosts with fewer active sessions
3. Affinity: prefer same host as parent session when sub-agent needs shared filesystem

```csharp
public sealed class ResourceAwarePlacementEngine : IPlacementEngine
{
    // Injected: IDbContextFactory (for active session counts),
    //           HostMetricCache (for live CPU/memory data)

    public PlacementDecision ChooseNode(string ownerUserId,
        SessionRequirements requirements, IReadOnlyList<NodeCapability> inventory)
    {
        // 1. Filter by hard constraints (existing logic)
        // 2. Score by: available memory, CPU headroom, active session count
        // 3. Apply affinity bonus if TargetHostId matches or parent session is on this host
        // 4. Return highest-scoring node
    }
}
```

**MODIFY: `NodeCapability`** (Abstractions.cs)

Add live resource data (currently CpuTotal and MemTotalMb are hardcoded to 0):

```csharp
public sealed record NodeCapability(
    // ... existing fields ...
    double CpuUsedPercent,       // NEW: from HostMetricPollingService
    int MemUsedMb,               // NEW
    int ActiveSessionCount       // NEW
);
```

**NEW: API Endpoints** (Program.cs)

```
POST /api/dispatch         -> CoordinationService.DispatchAsync()
POST /api/sessions/{id}/spawn -> CoordinationService.SpawnSubAgentAsync()
GET  /api/sessions/{id}/children -> CoordinationService.GetChildSessionsAsync()
```

**MODIFY: `HostMetricPollingService`** (Monitoring/)

Currently polls hosts for CPU/memory. Extend to populate a `HostMetricCache` (concurrent dictionary) that the placement engine reads. The polling service already does the SSH work; it just needs to expose results beyond SSE events.

**NEW: `HostMetricCache`** (Monitoring/HostMetricCache.cs)

In-memory singleton that `HostMetricPollingService` writes to and `ResourceAwarePlacementEngine` reads from:

```csharp
public sealed class HostMetricCache
{
    private readonly ConcurrentDictionary<string, HostMetricSnapshot> _snapshots = new();

    public void Update(string hostId, HostMetricSnapshot snapshot);
    public HostMetricSnapshot? Get(string hostId);
    public IReadOnlyDictionary<string, HostMetricSnapshot> GetAll();
}

public sealed record HostMetricSnapshot(
    double CpuPercent, long MemUsedMb, long MemTotalMb,
    int ActiveSessions, DateTimeOffset CollectedUtc);
```

### Data Flow: Dispatch

```
Client: POST /api/dispatch { tasks: [...], strategy: "Parallel" }
  -> CoordinationService.DispatchAsync()
     -> Generate dispatchId = Guid
     -> For each task:
        1. Attach dispatchId to StartSessionRequest
        2. SessionCoordinator.StartSessionAsync()  // reuses existing flow
           -> Placement engine picks host (now resource-aware)
           -> Backend starts session
           -> Session persisted with DispatchId
     -> Return all session IDs
     -> Emit DispatchStarted event to fleet stream
```

### Data Flow: Sub-Agent Spawn

```
Running session requests child agent (via coordinator API, not direct):
  POST /api/sessions/{parentId}/spawn { request }
  -> CoordinationService.SpawnSubAgentAsync()
     -> Verify parent session exists and is Running
     -> Set ParentSessionId on child request
     -> Placement: prefer parent's host if resources allow (affinity)
     -> SessionCoordinator.StartSessionAsync()  // reuses existing flow
     -> Link child to parent in DB
     -> Emit SubAgentSpawned event on parent's event stream
```

### Key Design Decision

Sub-agent communication goes through the coordinator, NOT direct agent-to-agent. This is explicitly in the project's Out of Scope section ("Agent-to-agent direct communication -- debugging nightmares, use orchestrator-mediated coordination"). The coordinator mediates: a parent session's output event can trigger the operator (or a future automation rule) to spawn a sub-agent. The parent does not call the child directly.

---

## Feature 3: Host Inventory Discovery

### What Changes

Extend host records with discovered capabilities: installed agent CLIs, their versions, available disk space, installed tools. Currently `HostRecord` has static config from `hosts.json` with no dynamic discovery.

### Integration Points

**NEW: `HostInventoryService`** (Config/HostInventoryService.cs)

Background service that periodically SSHes into hosts and runs discovery commands:

```csharp
public sealed class HostInventoryService : BackgroundService
{
    // Discovers: which agent CLIs are installed, their versions,
    // disk space, git version, available tools
    // Writes results to DB via HostEntity extensions
}
```

**MODIFY: `HostEntity`** (Data/Entities/)

Add inventory columns:

```csharp
public string? InventoryJson { get; set; }      // NEW: serialized HostInventory
public DateTimeOffset? LastInventoryUtc { get; set; }  // NEW
```

**NEW: `HostInventory`** (Contracts/Models.cs)

```csharp
public sealed record HostInventory(
    AgentCliInfo[] InstalledAgents,
    long DiskFreeMb,
    string? GitVersion,
    string[] AvailableTools,
    DateTimeOffset DiscoveredUtc);

public sealed record AgentCliInfo(
    string AgentType,     // e.g., "claude", "codex"
    string Version,
    string Path);
```

**MODIFY: `HostRecord`** (Contracts/Models.cs)

Add inventory to the DTO:

```csharp
public sealed record HostRecord(
    // ... existing fields ...
    HostInventory? Inventory = null);  // NEW
```

**MODIFY: `HostCommandProtocol`** (HostDaemon/HostCommandProtocol.cs)

Add `discover-inventory` command that the host daemon handles by running:
- `which claude && claude --version` (and similar for each known agent CLI)
- `df -h /` for disk space
- `git --version` for git availability

**MODIFY: `NodeCapability`** (Abstractions.cs)

Include discovered inventory so placement engine can filter by agent availability:

```csharp
public sealed record NodeCapability(
    // ... existing fields ...
    string[] InstalledAgents    // NEW: from inventory discovery
);
```

**MODIFY: `SimplePlacementEngine` / `ResourceAwarePlacementEngine`**

Filter candidates by whether the requested agent type (`ImageOrProfile`) is actually installed on the host. Currently placement does not verify this -- it assumes all hosts can run any agent. With inventory data, placement can reject hosts that lack the required agent CLI.

**NEW: API Endpoint**

```
GET /api/hosts/{hostId}/inventory -> HostInventory from DB
POST /api/hosts/{hostId}/discover -> trigger on-demand inventory refresh
```

**MODIFY: `HostMetricPollingService`**

Option A: Merge inventory discovery into existing polling (runs less frequently, every 5 min vs 30s for metrics).
Option B: Keep as separate `HostInventoryService` with its own interval.

Recommendation: **Option B** -- separate service. Inventory changes rarely (agent installs/updates). Run every 10 minutes or on-demand. Metric polling stays at 30s. Different concerns, different intervals.

### Data Flow

```
HostInventoryService (background, every 10 min):
  For each enabled host:
    1. SSH connect (reuse ISshHostConnectionFactory)
    2. Send "discover-inventory" HostCommand
    3. Parse response (installed agents, versions, disk space)
    4. Write HostEntity.InventoryJson + LastInventoryUtc to DB
    5. Update HostMetricCache with agent availability

On-demand:
  POST /api/hosts/{hostId}/discover
    -> HostInventoryService.DiscoverAsync(hostId)
    -> Same flow as above but for single host
```

---

## Feature 4: Git Worktree Isolation

### What Changes

Allow multiple agents on the same host to work on the same repository safely by using `git worktree` to create isolated working copies. v1.0 has `GitWorktreeProvider` and `WorktreeDescriptor` but the provider is a stub (creates a directory with a text file, does not actually call `git worktree`).

### Integration Points

**MODIFY: `GitWorktreeProvider`** (Storage/GitWorktreeProvider.cs)

Replace the stub with real git worktree operations. This runs on the coordinator and issues SSH commands to the target host:

```csharp
public sealed class GitWorktreeProvider : IWorktreeProvider
{
    private readonly ISshHostConnectionFactory _connectionFactory;
    // ... config for SSH credentials

    public async Task<string> EnsureMaterializedAsync(
        WorktreeDescriptor descriptor, string destinationRoot, CancellationToken ct)
    {
        // 1. Check if base repo clone exists on host
        //    ssh host "test -d /repos/{repoHash}/.git"
        // 2. If not, clone: ssh host "git clone {repoUrl} /repos/{repoHash}"
        // 3. Create worktree: ssh host "cd /repos/{repoHash} && git worktree add /worktrees/{worktreeId} {ref}"
        // 4. If sparse, configure sparse-checkout paths
        // 5. Return worktree path
    }
}
```

**MODIFY: `StartSessionPayload`** (HostDaemon/HostDaemonModels.cs)

The `WorkingDirectory` field already exists. Change `SshBackend.StartAsync()` to set it to the worktree path instead of `/sessions/{sessionId}` when a `WorktreeId` is specified in the request.

**MODIFY: `SshBackend.StartAsync()`** (Backends/SshBackend.cs)

Integrate worktree materialization before launching the agent:

```csharp
// In StartAsync, before sending start-session command:
if (!string.IsNullOrEmpty(request.WorktreeId))
{
    var descriptor = new WorktreeDescriptor(
        request.WorktreeId,
        request.Requirements.SharedStorageProfile ?? throw ...,
        request.Requirements.Labels?["ref"] ?? "main",
        Shallow: true, Sparse: false);

    var worktreePath = await _worktreeProvider.EnsureMaterializedAsync(
        descriptor, "/worktrees", ct);

    payload.WorkingDirectory = worktreePath;
    entity.WorktreePath = worktreePath;
}
```

**MODIFY: `StartSessionRequest`** (Contracts/Models.cs)

The `WorktreeId` field already exists. Add `RepoUrl` field (or use existing `SharedStorageProfile` as the repo URL -- current semantics are ambiguous, clarify):

```csharp
public sealed record StartSessionRequest(
    // ... existing fields ...
    string? RepoUrl = null  // NEW: git repo URL for worktree isolation
);
```

**NEW: `HostCommandProtocol` commands**

- `setup-worktree` -- clone base repo if needed, create worktree
- `cleanup-worktree` -- remove worktree after session ends (prune)

**MODIFY: `SshBackend.StopAsync()` / `CleanupConnection()`**

After session stops, clean up the worktree on the remote host:

```csharp
// In cleanup, if session had a worktree:
if (session.WorktreePath is not null)
{
    await SendCommandToSession(sessionId,
        HostCommandProtocol.CreateCleanupWorktree(session.WorktreePath), ct);
}
```

**NEW: `SessionEventKind.WorktreeCreated`, `SessionEventKind.WorktreeCleaned`**

Audit events for worktree lifecycle.

### Key Design Decision

The coordinator orchestrates worktree creation via SSH commands to the target host -- the host daemon does the actual `git worktree add/remove`. Do NOT clone repos on the coordinator machine. The coordinator never touches git directly; it sends commands to the host where the agent will run. This keeps the coordinator stateless w.r.t. repository data.

### Worktree Naming Convention

```
/repos/{sha256(repoUrl)}/           # Base clone (shared across sessions)
/worktrees/{sessionId}/             # Per-session worktree (isolated)
```

Using sessionId as the worktree name guarantees uniqueness and makes cleanup straightforward (delete worktree when session stops).

---

## Component Boundary Summary

### New Components

| Component | Project | Lifetime | Dependencies |
|-----------|---------|----------|--------------|
| `CoordinationService` | Orchestration | Singleton | SessionCoordinator, IDbContextFactory |
| `ResourceAwarePlacementEngine` | Orchestration | Singleton | HostMetricCache, IDbContextFactory |
| `HostMetricCache` | Orchestration | Singleton | None (written by HostMetricPollingService) |
| `HostInventoryService` | Orchestration | BackgroundService | ISshHostConnectionFactory, IDbContextFactory |

### Modified Components

| Component | Change Type | Impact |
|-----------|-------------|--------|
| `SessionCoordinator` | Add control signal routing | LOW -- new branch in SendInputAsync |
| `SshBackend` | Worktree integration, control signals | MEDIUM -- StartAsync and StopAsync flow changes |
| `SimplePlacementEngine` | Replace with ResourceAwarePlacementEngine | MEDIUM -- new scoring logic |
| `HostCommandProtocol` | 4 new commands | LOW -- additive, no breaking changes |
| `SessionEntity` | 2 new columns | LOW -- EF migration, nullable columns |
| `SessionState` enum | Add Paused | LOW -- new enum value, no existing value changes |
| `SendInputRequest` | Add Signal field | LOW -- optional field, backward compatible |
| `NodeCapability` | Add live metrics + inventory | MEDIUM -- all inventory calls need updating |
| `GitWorktreeProvider` | Full rewrite (stub -> real) | HIGH -- but isolated component |
| `HostMetricPollingService` | Write to HostMetricCache | LOW -- additive |

### Unchanged Components

| Component | Why No Change |
|-----------|---------------|
| `DurableEventService` | Already handles arbitrary event kinds via SessionEvent |
| `SseSubscriptionManager` | Channel<T> pattern works for all new event types |
| `ApprovalService` | Approval flow unchanged; interactive signals bypass approval |
| `ISanitizationService` | Sanitization runs on all inputs including redirects (existing pipeline) |
| `ISkillRegistry`, `ISkillPolicyService` | Policy model unchanged |
| `AgentHubDbContext` | Needs migration but schema changes are additive |

---

## Suggested Build Order

Dependencies between v1.1 features determine the build sequence:

### Phase 1: Foundations (no feature dependencies)

1. **Schema migration** -- Add `ParentSessionId`, `DispatchId` to SessionEntity; `InventoryJson`, `LastInventoryUtc` to HostEntity; `Paused` to SessionState enum. One migration, all features need it.
2. **HostMetricCache** -- Extract from HostMetricPollingService. Simple ConcurrentDictionary wrapper. Both placement and coordination depend on this.
3. **Contract changes** -- `SessionControlSignal`, `DispatchRequest`, `HostInventory`, `AgentCliInfo`. Pure DTOs, no logic.

### Phase 2: Host Inventory (independent, enables placement)

4. **HostInventoryService** -- Background discovery via SSH. Depends on schema migration.
5. **HostCommandProtocol: discover-inventory** -- Additive protocol extension.
6. **API: GET /api/hosts/{id}/inventory** -- Thin endpoint over DB data.

### Phase 3: Interactive Sessions (independent of coordination)

7. **SendInputRequest + SessionControlSignal** -- Contract ready from Phase 1.
8. **SessionCoordinator control signal routing** -- Pause/Resume/Redirect logic.
9. **HostCommandProtocol: pause-session, resume-session** -- Protocol extensions.
10. **SshBackend control signal handling** -- Route signals to HostCommands.

### Phase 4: Git Worktree Isolation (independent, but test with interactive)

11. **GitWorktreeProvider rewrite** -- Real git worktree via SSH commands.
12. **SshBackend worktree integration** -- Wire into StartAsync/StopAsync.
13. **HostCommandProtocol: setup-worktree, cleanup-worktree** -- Protocol extensions.
14. **Worktree cleanup on session stop** -- Lifecycle management.

### Phase 5: Multi-Agent Coordination (depends on Phases 2-4)

15. **ResourceAwarePlacementEngine** -- Replaces SimplePlacementEngine. Needs HostMetricCache (Phase 1) and inventory data (Phase 2).
16. **CoordinationService** -- Dispatch and sub-agent spawning. Needs placement engine (this phase) and optionally worktrees (Phase 4) for shared-repo scenarios.
17. **API: dispatch, spawn, children endpoints** -- Thin layer over CoordinationService.

### Build Order Rationale

- **Inventory before coordination**: The resource-aware placement engine needs host inventory data to make good decisions. Building inventory first means coordination has accurate placement from day one.
- **Interactive before coordination**: Pause/resume semantics need to work on single sessions before orchestrating multiple sessions. Also simpler to test in isolation.
- **Worktrees before coordination**: When dispatching multiple agents to work on the same repo, worktree isolation prevents conflicts. Having worktrees working first means dispatch can use them immediately.
- **Coordination last**: It is the most complex feature and depends on all three others being solid. The `CoordinationService` is a thin orchestration layer over existing `SessionCoordinator` -- it reuses the full session lifecycle for each child/dispatched session.

### Dependency Graph

```
Schema Migration ──────────────────────────────┐
       │                                        │
HostMetricCache ────────────────────┐           │
       │                            │           │
Contract DTOs ──┬───────────────────┤           │
                │                   │           │
     HostInventoryService    Interactive     Worktrees
     (Phase 2)               Sessions       (Phase 4)
                │            (Phase 3)          │
                │                   │           │
                └───────┬───────────┘           │
                        │                       │
              ResourceAwarePlacement ◄──────────┘
                        │
              CoordinationService
              (Phase 5)
```

---

## Risk Assessment

| Integration Area | Risk | Mitigation |
|------------------|------|------------|
| Pause/Resume via SSH | MEDIUM -- Not all agent CLIs support suspension (SIGTSTP) | Graceful degradation: if agent ignores SIGTSTP, treat Pause as "stop accepting input" rather than process suspension. Buffer new inputs until Resume. |
| Git worktree over SSH | MEDIUM -- git operations can fail (auth, disk space, network) | Emit `WorktreeFailed` event and fall back to `/sessions/{id}` temp directory. Session starts without worktree isolation rather than failing entirely. |
| Resource-aware placement accuracy | LOW -- Metric staleness (30s poll interval) | Acceptable for 5-10 hosts. Placement is best-effort; worst case is suboptimal host choice, not failure. |
| Parent-child session lifecycle | MEDIUM -- What happens to children when parent stops? | Policy choice: orphan children (keep running) vs cascade stop. Recommend orphan-by-default with optional `cascadeStop: true` on dispatch request. |
| HostCommandProtocol versioning | LOW -- Adding new commands to hosts that may not support them | Protocol already has a `version` field. New commands return `{ success: false, error: "unknown command" }` on old daemons. |

## Sources

- Direct analysis of existing codebase: `src/AgentHub.Orchestration/` (12,000 LOC, 178 files) -- HIGH confidence
- `git worktree` documentation: https://git-scm.com/docs/git-worktree -- HIGH confidence
- Existing `HostCommandProtocol` contract: single-line JSON over SSH stdin/stdout -- HIGH confidence
- v1.0 PROJECT.md: explicit out-of-scope items (no agent-to-agent communication) -- HIGH confidence
- .NET `ConcurrentDictionary` patterns for `HostMetricCache` -- standard .NET, HIGH confidence

---
*Architecture research for: AgentSafeEnv v1.1 Multi-Agent & Interactive milestone*
*Researched: 2026-03-09*
