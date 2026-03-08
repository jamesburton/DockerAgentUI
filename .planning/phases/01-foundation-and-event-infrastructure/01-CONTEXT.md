# Phase 1: Foundation and Event Infrastructure - Context

**Gathered:** 2026-03-08
**Status:** Ready for planning

<domain>
## Phase Boundary

Persistent data layer, coordinator API with REST endpoints, real-time SSE event streaming with replay support, and agent adapter abstraction with Claude Code as first implementation. This phase makes the platform durable and establishes the abstractions that Phase 2 builds execution on top of.

</domain>

<decisions>
## Implementation Decisions

### Scaffold Approach
- Claude evaluates each piece of the existing scaffold against Phase 1 requirements
- Existing contracts/models (SessionSummary, SessionEvent, HostRecord, SkillManifest, enums) are a solid starting point — refine as needed
- Existing interfaces (ISessionBackend, ISessionCoordinator, etc.) are well-structured abstractions — keep what serves Phase 1, rewrite all implementations
- 5-project solution structure (Contracts, Orchestration, Service, AppHost, Maui) — Claude decides if this is the right split or needs adjustment
- Claude picks the fastest path to a working foundation (get it compiling first vs build incrementally)

### Data Model & Persistence
- EF Core with SQLite as first provider, pluggable for future swap to Postgres/SQL Server/SpacetimeDB
- **What persists:** Sessions (active + history), events/output, host inventory
- **Config on disk:** Skills, policies, agent definitions stay as files on disk (JSON/MD) — agents can already read these formats. Server-side DB persistence for config is a future iteration, but design the abstraction so it's swappable.
- Config must be copyable between servers, hosts, and sessions. Support deployment of .md-based elements into subfolders, with host-level and project-level options.
- **Host inventory:** Seed from hosts.json on startup, track runtime state in DB
- **Event storage:** Claude decides based on SQLite constraints and typical session sizes (DB table vs DB + file fallback for large sessions)

### Event Streaming Design
- SSE with event IDs and Last-Event-ID replay support (research flagged this as critical gap)
- **Two stream types:** Per-session (/api/sessions/{id}/events) AND fleet-wide (/api/events) for dashboard use
- Event types: Claude evaluates current SessionEventKind and extends if needed
- Replay depth: Claude picks reasonable defaults with configuration
- Replace or evolve the existing in-memory SessionEventBus (System.Threading.Channels) to support durable event persistence

### Agent Adapter Pattern
- Claude decides whether to introduce a separate IAgentAdapter interface (transport vs agent concern separation) based on what makes Phase 2 cleaner
- **Claude Code as first adapter:** Standard CLI wrapping via CliWrap as default invocation method
- **MCP connection also supported:** Configuration assistant (script/skill) to set up MCP with minimal effort. CLI wrapping is the default; MCP is an alternative when configured.
- **Permission flags:** Both per-agent-type defaults AND per-session overrides. Global agent definitions in config/agents/*.json, per-host overrides in host config (e.g., different paths on Windows vs Linux).
- Use --output-format json where agents support it. Version-aware adapters (research flagged CLI format instability).

### Claude's Discretion
- Whether to get the scaffold compiling first or build incrementally from scratch
- Which existing interfaces to keep vs redesign
- Project structure adjustments
- EF Core entity design and migration strategy
- Event storage approach (all-in-DB vs hybrid)
- Event type extensions beyond current enum
- SSE replay depth defaults
- Whether IAgentAdapter is a separate interface from ISessionBackend

</decisions>

<specifics>
## Specific Ideas

- Config files should be portable — copyable between servers, hosts, and sessions
- Support .md-based config elements (like AGENTS.md, PLANS.md, skill definitions) deployed into subfolders
- Host-level AND project-level configuration options
- MCP setup should be as easy as possible — a script or skill that handles the wiring
- The adapter pattern must support per-agent permission flag defaults (e.g., `--dangerously-skip-permissions` always on for sandboxed agents)

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- **Models.cs (Contracts):** SessionSummary, SessionEvent, HostRecord, SkillManifest, StartSessionRequest, SendInputRequest — well-designed DTOs, good starting point
- **Abstractions.cs (Orchestration):** 9 interfaces including ISessionBackend, ISessionCoordinator, IPlacementEngine, ISanitizationService, ISkillRegistry — solid abstraction layer
- **SessionEventBus (Service):** System.Threading.Channels-based pub/sub — pattern is correct, needs durable backing
- **Config files (config/):** hosts.json, global.policy.json, skills/*.json — good examples of the file-based config approach

### Established Patterns
- DI registration in Program.cs with concrete implementations per interface
- JSON config loading with JsonSerializerDefaults.Web (camelCase)
- Minimal API endpoint mapping pattern
- Multi-backend registration (InMemoryBackend, SshBackend both registered as ISessionBackend)

### Integration Points
- Service/Program.cs is the composition root — all DI wiring happens here
- AppHost project for Aspire orchestration — currently minimal
- API endpoints at /api/sessions, /api/hosts, /api/skills, /api/policy, /healthz
- SSE endpoint at /api/sessions/{sessionId}/events

</code_context>

<deferred>
## Deferred Ideas

- SpacetimeDB as an EF Core provider alternative — research feasibility when needed
- Database-backed config persistence (skills, policies, agent definitions) — next iteration after file-based is working
- MCP as primary agent control protocol (vs CLI wrapping) — evaluate after Claude Code adapter proves the pattern
- Config hot-reload / notification system — useful but not Phase 1

</deferred>

---

*Phase: 01-foundation-and-event-infrastructure*
*Context gathered: 2026-03-08*
