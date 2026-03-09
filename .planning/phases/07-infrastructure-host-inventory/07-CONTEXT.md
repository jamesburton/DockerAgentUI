# Phase 7: Infrastructure & Host Inventory - Context

**Gathered:** 2026-03-09
**Status:** Ready for planning

<domain>
## Phase Boundary

Add EF Core schema columns (ParentSessionId, DispatchId, InventoryJson) without breaking existing data, build an in-memory HostMetricCache for the placement engine, and implement SSH-based agent CLI discovery with version detection, disk space monitoring, and cached inventory with on-demand refresh. Operators see exactly what is installed on every host before dispatching work.

</domain>

<decisions>
## Implementation Decisions

### Inventory data model
- Rich capability profile: InventoryJson stores agents (name, version, path, capabilities), diskFreeGb, and gitVersion
- Agent capabilities derived from config-driven version mapping (not runtime probing)
- Disk space lives inside InventoryJson (not a separate DB column) — refreshes on inventory cycle
- Git version included in inventory (Phase 9 reads it for worktree support verification)

### Discovery mechanism
- Single composite SSH command per host — probes all agents + git + disk in one call, returns structured JSON
- Probe all agents defined in agents.json by default on every host
- Per-host overrides supported: skip certain probes or specify custom CLI paths
- Path lookup: default to `which` (Linux/macOS) / `where` (Windows), but custom path in host config takes precedence and skips PATH lookup
- OS-appropriate commands following established HostMetricPollingService pattern (Windows-first priority)

### Dashboard inventory display — Web
- Expandable host card in HostSidebar: click to reveal installed agents with versions, disk space, and git version
- Collapsed state unchanged (name, status, CPU bar, memory bar, session count)
- Agent display: name + version only (capabilities are internal for placement engine, not shown in UI)
- Per-host refresh button (↻) in the expanded inventory section

### Dashboard inventory display — CLI
- Add Agents and Disk columns to existing `ah host status` table
- Agents column shows comma-separated names (e.g., "claude,codex")
- Disk column shows free GB
- No separate inventory command — one table for all host info

### Cache & refresh behavior
- Separate HostInventoryPollingService with 1-hour default TTL (configurable)
- HostMetricPollingService stays at 30s for CPU/memory (unchanged)
- On-demand refresh: per-host (POST /api/hosts/{hostId}/refresh-inventory) and refresh-all (POST /api/hosts/refresh-inventory)
- Cache auto-invalidated when system applies changes to a host
- Manual refresh from UI (per-host button) or API without waiting for next poll cycle

### HostMetricCache (INFRA-02)
- In-memory singleton cache (ConcurrentDictionary<string, HostSnapshot>)
- Holds latest metrics + inventory per host
- Updated by both polling services (metrics at 30s, inventory at 1hr)
- Placement engine reads from cache instead of DB — no DB round-trip during dispatch
- Exposes: Update(), Get(hostId), GetAll()

### Schema migration (INFRA-01)
- Add ParentSessionId (string?, nullable FK to Sessions) to SessionEntity
- Add DispatchId (string?, nullable) to SessionEntity
- Add InventoryJson (string?, nullable) to HostEntity
- Single EF Core migration, non-breaking (all nullable columns)

### Claude's Discretion
- agents.json config file location and format details
- Composite SSH probe script implementation per OS
- HostSnapshot record shape and cache eviction details
- Expandable card animation/transition in MudBlazor
- Inventory SSE event design (if needed for real-time cache updates)
- Error handling for SSH probe failures (partial results vs full failure)

</decisions>

<specifics>
## Specific Ideas

- Inventory polling at 1 hour with manual refresh — "cache this for 1hr but should have a refresh option triggered manually or when we have applied changes"
- agents.json as the single source of truth for known agent CLIs, their version flags, and version-to-capability mappings
- Per-host overrides allow custom paths without changing the global config — important for non-standard installs
- Expandable host card keeps sidebar clean by default, detail on demand

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- **HostMetricPollingService**: Reference pattern for HostInventoryPollingService — BackgroundService with SSH calls, OS-specific commands, DB persistence
- **HostEntity**: Already has metric fields (CpuPercent, MemUsedMb, etc.) — add InventoryJson alongside
- **SessionEntity**: Already has WorktreePath, CleanupPolicy — add ParentSessionId, DispatchId alongside
- **EntityMappers.cs**: Bidirectional HostEntity ↔ HostRecord mapping — extend for inventory fields
- **HostSidebar.razor**: Existing host cards with CPU/memory bars — extend with expandable inventory section
- **HostStatusCommand.cs**: Existing CLI table with Spectre.Console — add Agents and Disk columns
- **ISshHostConnectionFactory**: Testable SSH connection creation — reuse for inventory probing
- **HostSeedingService**: Config-driven host initialization pattern — similar approach for agents.json

### Established Patterns
- Background services use IDbContextFactory<AgentHubDbContext> for scoped DB access
- All core services registered as Singletons
- OS detection per host for SSH commands (Windows/Linux/macOS)
- Host config from hosts.json, seeded on startup via HostSeedingService
- SSE events emitted via DurableEventService for real-time dashboard updates
- Metric fields on HostEntity updated by polling service, mapped to HostRecord DTO

### Integration Points
- **HostEntity**: Add InventoryJson column, extend ToDto() mapping
- **SessionEntity**: Add ParentSessionId and DispatchId columns
- **HostRecord (Contracts)**: Add inventory-related properties for API consumers
- **Program.cs**: Register HostInventoryPollingService, HostMetricCache
- **SimplePlacementEngine**: Inject HostMetricCache instead of DB queries
- **DashboardApiClient / AgentHubApiClient**: Add refresh-inventory API calls
- **API endpoints**: Add POST /api/hosts/{hostId}/refresh-inventory and POST /api/hosts/refresh-inventory

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 07-infrastructure-host-inventory*
*Context gathered: 2026-03-09*
