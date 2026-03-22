# Phase 10: Multi-Agent Coordination - Context

**Gathered:** 2026-03-22
**Status:** Ready for planning

<domain>
## Phase Boundary

Operators can dispatch work across multiple machines with automatic placement and parent-child session tracking. A running session can spawn child sessions on other hosts via the coordinator API. Parent-child relationships are tracked in the database, child events are visible on the parent's SSE stream, and the placement engine uses weighted scoring for host selection. Depth and count limits prevent cascade runaway. Batch API, dependency DAGs, and token budgets are deferred to v1.2 (COORD-10, COORD-11, COORD-12).

</domain>

<decisions>
## Implementation Decisions

### Spawning trigger & mechanisms
- Both operators (CLI/Web UI) and running agents can spawn child sessions
- Two spawn mechanisms in Phase 10:
  1. **Coordinator HTTP API** — Agent calls POST /api/sessions with ParentSessionId. Works with any agent that can make HTTP calls
  2. **SSH stdout intercept** — Agent writes a special spawn command to stdout; PTY reader intercepts and forwards to coordinator. Works with existing PTY streaming pipeline
- MCP tool mechanism deferred to the MCP protocol phase (v1.2+)
- Child sessions inherit parent's agent type, accept-risk, and worktree mode by default, but spawn request can override any setting

### Parent-child event routing
- Child events appear inline on parent's SSE stream with `[child-id]` prefix — matches fleet-wide stream pattern
- Default: parent stream gets lifecycle events only (ChildSpawned, ChildCompleted, ChildFailed)
- Opt-in full child output via query param (e.g., `?includeChildren=true`) — prevents stream flooding
- When parent session stops, children continue running independently (orphan); parent stop emits a warning that children are still active; operator must stop them manually

### Dashboard visualization
- **Web**: Tree view in session list — parent sessions with expandable children indented underneath. Child sessions also accessible standalone
- **CLI**: Indented tree in `ah session list` using Spectre.Console Tree component. Tree connectors (├──) for visual hierarchy

### Placement scoring (COORD-04)
- Weighted scoring of three factors: available CPU%, free memory, active session count
- Disk space as a hard filter (reject below threshold), not a scoring factor
- Weights configurable in appsettings.json (e.g., CpuWeight: 0.4, MemoryWeight: 0.3, SessionWeight: 0.3) with sensible defaults
- TargetHostId override remains — if set, skip scoring and use that host (existing behavior)
- Hosts with stale metrics (older than 2x polling interval) get a penalty score; hosts with no metrics excluded unless explicitly targeted

### Cascade limits & safety (COORD-05)
- Maximum nesting depth: 3 (parent → child → grandchild → great-grandchild), configurable in appsettings
- Maximum children per parent: configurable, derived from host capabilities, overridable per session and per task
- Per-host concurrent session limit (e.g., 3 per host) — no global cap, natural ceiling based on registered hosts
- When a spawn request exceeds depth or count limit: reject with HTTP 400/429 and clear error explaining which limit was hit and current values

### Claude's Discretion
- SSH stdout spawn command format and intercept pattern
- Exact placement scoring formula and normalization approach
- ChildSpawned/ChildCompleted/ChildFailed event payload design
- Per-host session limit storage (appsettings vs host config vs DB column)
- Tree view component implementation details in MudBlazor
- Spawn request DTO design (extend StartSessionRequest vs new SpawnRequest)
- Stale metric penalty magnitude and threshold calculation

</decisions>

<specifics>
## Specific Ideas

- ParentSessionId and DispatchId columns already exist on SessionEntity (added in Phase 7 migration) — ready to use
- HostMetricCache already provides real-time CPU/memory/inventory per host — placement engine reads from cache
- SimplePlacementEngine currently just filters — upgrade to weighted scoring while preserving existing filter logic
- SSH stdout intercept builds on the PTY ShellStream reader from Phase 9 UAT fixes — look for a spawn marker pattern in output
- Orphan-on-parent-stop (not cascade-stop) — keeps children running independently, avoids complex coordinated shutdown

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- **SessionEntity.ParentSessionId** (Data/Entities/): Nullable FK column already in schema — wire up for parent-child tracking
- **SessionEntity.DispatchId** (Data/Entities/): Nullable column for grouping related sessions
- **HostMetricCache** (Monitoring/): ConcurrentDictionary with CPU/memory/inventory snapshots — feed to placement scoring
- **SimplePlacementEngine** (Placement/): Existing filter-based placement — extend with weighted scoring
- **DurableEventService** (Events/): Persists events + SSE broadcast — route child events to parent stream
- **SshHostConnection.StartStreamingCommandAsync**: PTY-allocated ShellStream — intercept spawn commands in output
- **SessionCoordinator.StartSessionAsync**: Orchestrates placement + backend dispatch — add parent-child wiring
- **Spectre.Console Tree**: Available for CLI tree rendering (already in dependencies)

### Established Patterns
- Background services use IDbContextFactory for scoped DB access
- Events emitted via Func<SessionEvent, Task> callback through SessionCoordinator
- SSE events flow: DurableEventService → SseSubscriptionManager → per-session + fleet-wide channels
- NodeCapability record feeds placement engine from backend inventory
- Configuration via appsettings.json with IOptions<T> pattern

### Integration Points
- **SimplePlacementEngine.ChooseNode**: Replace FirstOrDefault with scored ranking
- **SessionCoordinator.StartSessionAsync**: Set ParentSessionId on child session entity
- **DurableEventService.EmitAsync**: Forward child events to parent's SSE channel
- **SseSubscriptionManager**: Support includeChildren query param for parent subscriptions
- **Program.cs API endpoints**: Add spawn endpoint, parent-child query filters
- **SessionWatchCommand / SessionDetail.razor**: Tree rendering for parent-child
- **FleetOverview.razor**: Show parent-child relationships in fleet view

</code_context>

<deferred>
## Deferred Ideas

- MCP tool as third spawn mechanism — future MCP protocol phase
- Batch API for launching N sessions in one call (COORD-10) — v1.2
- Session dependency DAG with fan-out/fan-in execution (COORD-11) — v1.2
- Token/cost budget inheritance from parent to child (COORD-12) — v1.2

</deferred>

---

*Phase: 10-multi-agent-coordination*
*Context gathered: 2026-03-22*
