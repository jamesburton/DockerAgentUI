# Architecture Research

**Domain:** Multi-agent orchestration control plane
**Researched:** 2026-03-08
**Confidence:** HIGH

## Standard Architecture

### System Overview

```
                          Clients
    ┌──────────┐    ┌──────────┐    ┌──────────┐
    │   CLI    │    │  Blazor  │    │   MAUI   │
    └────┬─────┘    └────┬─────┘    └────┬─────┘
         │               │               │
         └───────────────┼───────────────┘
                         │ HTTP/SSE
    ┌────────────────────┴─────────────────────────────┐
    │              Coordinator API (ASP.NET Core)       │
    │  ┌───────────┐ ┌───────────┐ ┌────────────────┐  │
    │  │ Session   │ │ Event     │ │ Host/Skill     │  │
    │  │ Endpoints │ │ Streaming │ │ Config Endpts  │  │
    │  └─────┬─────┘ └─────┬─────┘ └───────┬────────┘  │
    │        │             │               │            │
    │  ┌─────┴─────────────┴───────────────┴─────────┐  │
    │  │          Session Coordinator                 │  │
    │  │  (placement + policy + sanitization + route) │  │
    │  └─────┬──────────────┬────────────────┬───────┘  │
    ├────────┼──────────────┼────────────────┼──────────┤
    │    Backends           │                │          │
    │  ┌─────┴────┐  ┌─────┴─────┐  ┌──────┴──────┐   │
    │  │ InMemory │  │    SSH    │  │   Nomad     │   │
    │  │ (dev)    │  │ Backend  │  │  Backend    │   │
    │  └──────────┘  └─────┬─────┘  └──────┬──────┘   │
    └──────────────────────┼───────────────┼───────────┘
                           │               │
              ┌────────────┴─┐    ┌────────┴──────────┐
              │ Remote Hosts │    │ Nomad Cluster     │
              │ (SSH+Agent   │    │ (Scheduler-backed │
              │  CLI procs)  │    │  agent execution) │
              └──────────────┘    └───────────────────┘

    ┌──────────────────────────────────────────────────┐
    │                  Data Layer                       │
    │  ┌──────────┐  ┌───────────┐  ┌──────────────┐   │
    │  │ EF Core  │  │ Git       │  │ Blob         │   │
    │  │ (session │  │ Worktrees │  │ Storage      │   │
    │  │  + host  │  │ (source)  │  │ (artifacts)  │   │
    │  │  state)  │  │           │  │              │   │
    │  └──────────┘  └───────────┘  └──────────────┘   │
    └──────────────────────────────────────────────────┘

    ┌──────────────────────────────────────────────────┐
    │              Cross-Cutting                        │
    │  ┌───────────┐ ┌───────────┐ ┌───────────────┐   │
    │  │ Aspire    │ │ Config    │ │ Sanitization  │   │
    │  │ (service  │ │ (skills,  │ │ & Policy      │   │
    │  │  discov., │ │  policies,│ │ Engine        │   │
    │  │  telemetry│ │  hosts)   │ │               │   │
    │  │  health)  │ │           │ │               │   │
    │  └───────────┘ └───────────┘ └───────────────┘   │
    └──────────────────────────────────────────────────┘
```

### Component Responsibilities

| Component | Responsibility | Typical Implementation |
|-----------|----------------|------------------------|
| **Coordinator API** | User-facing HTTP/SSE surface; routes requests to backends | ASP.NET Core minimal APIs with `TypedResults.ServerSentEvents` (.NET 10) |
| **Session Coordinator** | Orchestrates session lifecycle: placement decision, policy check, sanitization, backend routing | Stateless service aggregating backends, placement engine, policy, sanitizer |
| **Placement Engine** | Selects which backend+host fulfills a session request based on requirements and inventory | Pluggable `IPlacementEngine` - filter-based today, score-based later |
| **Execution Backends** | Actually start/stop/interact with agent processes on target infrastructure | `ISessionBackend` implementations: InMemory (dev), SSH (direct), Nomad (scheduled) |
| **Event Bus** | Fan-out of session events to SSE subscribers | In-process `Channel<T>` today; Redis/NATS for durability later |
| **Config Layer** | Skills, policies, host registry loaded from JSON files | JSON files in `/config/`; migrate to DB-backed when needed |
| **Sanitization & Policy** | Gate agent inputs: block dangerous commands, enforce skill allowlists | `ISanitizationService` + `ISkillPolicyService` pipeline before backend dispatch |
| **Storage** | Source code sharing and artifact persistence | Git worktrees for source; blob storage for artifacts/snapshots |
| **Data Layer** | Durable session state, host records, audit trail | EF Core with pluggable provider (SQLite for dev, Postgres/SQL Server for prod) |
| **Aspire AppHost** | Local dev orchestration, service discovery, telemetry, health checks | .NET Aspire AppHost project wiring all services together |

## Recommended Project Structure

```
src/
├── AgentHub.AppHost/              # Aspire orchestrator (dev environment)
│   └── Program.cs                 # Service wiring, resource declarations
├── AgentHub.Contracts/            # Shared DTOs, enums, record types
│   ├── Models.cs                  # Session, host, skill, event records
│   └── Events/                    # Strongly-typed event definitions (future)
├── AgentHub.Orchestration/        # Core business logic (no web dependency)
│   ├── Abstractions.cs            # Interface contracts
│   ├── Backends/                  # ISessionBackend implementations
│   │   ├── InMemoryBackend.cs     # Dev/test backend
│   │   ├── SshBackend.cs          # Direct SSH execution
│   │   └── NomadBackend.cs        # Nomad scheduler execution
│   ├── Coordinator/               # Session coordinator + workflow
│   │   └── SessionCoordinator.cs  # Placement + policy + dispatch
│   ├── Placement/                 # Node selection logic
│   │   └── SimplePlacementEngine.cs
│   ├── Config/                    # Skill, policy, host config loading
│   │   ├── HostRegistry.cs
│   │   ├── SkillRegistry.cs
│   │   ├── SkillPolicyService.cs
│   │   └── PolicyModels.cs
│   ├── Security/                  # Sanitization, input validation
│   │   └── BasicSanitizationService.cs
│   └── Storage/                   # Git worktree + blob providers
│       ├── GitWorktreeProvider.cs
│       └── BlobSharedStorageProvider.cs
├── AgentHub.Data/                 # (NEW) EF Core DbContext, migrations
│   ├── AgentHubDbContext.cs
│   ├── Entities/                  # DB entities (distinct from DTOs)
│   └── Migrations/
├── AgentHub.Service/              # ASP.NET Core web host
│   └── Program.cs                 # Endpoints, DI wiring, SSE setup
├── AgentHub.Cli/                  # (NEW) CLI client
│   └── Program.cs                 # Spectre.Console or System.CommandLine
├── AgentHub.Web/                  # (NEW) Blazor dashboard
│   └── ...
├── AgentHub.Maui/                 # MAUI desktop client (later)
│   └── ...
└── AgentHub.ServiceDefaults/      # (NEW) Aspire service defaults
    └── Extensions.cs              # Shared telemetry, health, resilience
```

### Structure Rationale

- **AgentHub.Contracts/** stays dependency-free so all projects can reference it without circular dependencies.
- **AgentHub.Orchestration/** has zero web framework dependency -- it can be tested in isolation and referenced by backends, CLI, or service.
- **AgentHub.Data/** is separated from Orchestration so the EF Core dependency does not leak into business logic. Orchestration talks to data through interfaces.
- **AgentHub.Service/** is thin -- it wires DI, defines HTTP endpoints, and delegates everything to the Orchestration layer.
- **AgentHub.ServiceDefaults/** follows the Aspire convention for shared service configuration (OpenTelemetry, health checks, resilience policies).

## Architectural Patterns

### Pattern 1: Hub-and-Spoke Orchestration (Supervisor)

**What:** A central coordinator receives all client requests, decomposes them, routes to the appropriate backend, and aggregates results/events back to the client. Agents do not talk to each other directly.
**When to use:** When you need predictable control flow, centralized policy enforcement, and simplified debugging. This is the right default for a fleet management platform where the operator needs visibility into everything.
**Trade-offs:** The coordinator is a potential bottleneck and single point of failure. At 5-10 machines this is not a concern. At 100+ machines, consider partitioning by backend or region.

This is the pattern the scaffold already follows and it is correct for this use case. The Coordinator API is the hub; backends and hosts are the spokes. Do not add peer-to-peer agent communication in v1.

### Pattern 2: Backend Abstraction (Strategy Pattern)

**What:** All execution backends implement `ISessionBackend` with identical contracts. The coordinator selects which backend to use at runtime via the placement engine. Adding a new backend (containers, cloud VMs) requires implementing one interface.
**When to use:** When you have multiple execution environments with different capabilities and constraints but want a unified control plane above them.
**Trade-offs:** The interface must be general enough to cover all backends. Backend-specific features require extension points (the `Extra` dictionary in `PlacementDecision` handles this). Be careful not to let the lowest-common-denominator interface prevent you from using backend-specific capabilities.

**The scaffold already implements this well.** The `ISessionBackend` interface is clean and the right level of abstraction.

### Pattern 3: Event Sourcing Lite (Event Stream as Primary Output)

**What:** Instead of polling for session state, the system emits a stream of `SessionEvent` records. Clients subscribe via SSE and receive real-time updates. The event stream is the canonical record of what happened.
**When to use:** When real-time visibility is a core value proposition. When you need audit trails. When multiple clients may watch the same session.
**Trade-offs:** Events need to be durable for replay (the current in-memory `Channel<T>` is volatile). Need a strategy for late-joining subscribers (replay from stored events vs. current-state-only). SSE is unidirectional -- input goes via POST, output comes via SSE.

**Implementation note for .NET 10:** The scaffold already uses `TypedResults.ServerSentEvents()` with `IAsyncEnumerable<SessionEvent>`. This is the correct .NET 10 pattern. Add `Last-Event-ID` header support for reconnection resilience.

### Pattern 4: Three-Tier State Separation

**What:** Separate session context (live conversation/output, ephemeral), task state (workflow checkpoints, durable), and system state (policies, skills, host registry, configuration). Do not conflate them.
**When to use:** Always. Mixing ephemeral session output with durable configuration leads to data model confusion and scaling problems.
**Trade-offs:** Requires discipline in the data model. More tables/stores, but each has clear lifecycle and caching characteristics.

```
System State (config)    → JSON files / DB rows, rarely changes, cached aggressively
Task State (sessions)    → EF Core entities, durable, queryable
Session Context (events) → Event stream, append-only, ephemeral or persisted to log
```

## Data Flow

### Session Lifecycle Flow

```
Client (CLI/Web/MAUI)
    │
    │ POST /api/sessions {StartSessionRequest}
    ▼
Coordinator API
    │
    ▼
SessionCoordinator.StartSessionAsync()
    │
    ├── 1. Gather inventory from all backends
    │       backend.GetInventoryAsync() × N backends
    │
    ├── 2. Placement engine selects backend + node
    │       placement.ChooseNode(requirements, inventory)
    │
    ├── 3. Selected backend starts session
    │       backend.StartAsync(request, placement, emit)
    │       └── Emits: StateChanged → "Running"
    │       └── Emits: Info → provisioning details
    │
    └── 4. Return session ID to client
            ← 201 Created {sessionId}
```

### Input Processing Flow

```
Client
    │
    │ POST /api/sessions/{id}/input {SendInputRequest}
    ▼
SessionCoordinator.SendInputAsync()
    │
    ├── 1. Look up session (verify ownership)
    │
    ├── 2. Policy check
    │       policy.IsAllowed(skillId, session, elevated)
    │       └── DENY → Emit Policy event, throw
    │
    ├── 3. Sanitization check
    │       sanitizer.Evaluate(request, session, skill)
    │       └── BLOCK → Emit Threat event, throw
    │
    ├── 4. Dispatch to backend
    │       backend.SendInputAsync(sessionId, sanitizedInput)
    │       └── Backend executes CLI command on remote host
    │       └── Emits: StdOut, StdErr, Metric events
    │
    └── 5. Emit Audit event (input accepted)
```

### Event Streaming Flow

```
Client
    │
    │ GET /api/sessions/{id}/events (SSE)
    ▼
SessionEventBus.Subscribe(sessionId)
    │
    ├── Creates Channel<SessionEvent> per subscriber
    ├── Registers writer in ConcurrentDictionary
    ├── Emits initial "SSE connected" event
    │
    ▼
    IAsyncEnumerable<SessionEvent> → TypedResults.ServerSentEvents()
    │
    │  (events arrive as backends emit them)
    │  ← SessionEvent {Kind, Data, Meta, Timestamp}
    │  ← SessionEvent ...
    │  (connection held open until cancelled)
```

### Key Data Flows

1. **Session creation:** Client -> API -> Coordinator -> PlacementEngine -> Backend -> Remote Host. Response flows back with session ID; events flow asynchronously via SSE.
2. **Agent output:** Remote Host process -> Backend (captures stdout/stderr) -> SessionEvent -> EventBus -> SSE -> Client. This is the primary real-time data flow.
3. **Configuration loading:** JSON files on disk -> Config services (HostRegistry, SkillRegistry, PolicyService) -> injected into Coordinator at startup. Hot-reload is a future enhancement.
4. **Storage materialization:** Session start triggers worktree checkout on target host; artifacts upload to blob storage during/after session.

## Scaling Considerations

| Scale | Architecture Adjustments |
|-------|--------------------------|
| 1-10 hosts (target v1) | Single coordinator process, in-memory event bus, SQLite, JSON config. This is fine. Do not over-engineer. |
| 10-50 hosts | Replace in-memory event bus with Redis Pub/Sub or NATS. Move session state to Postgres. Add host health-check background service. |
| 50+ hosts | Partition backends by region/type. Consider separate coordinator instances per backend type. Event persistence for replay. Horizontal API scaling behind load balancer. |

### Scaling Priorities

1. **First bottleneck: Event bus.** The in-memory `ConcurrentDictionary<string, ConcurrentBag<ChannelWriter<SessionEvent>>>` will lose events on process restart and does not support multi-instance. Replace with Redis Pub/Sub when you need persistence or horizontal scaling. Aspire has a Redis integration that makes this straightforward.
2. **Second bottleneck: Session state.** In-memory session tracking in backends means all state is lost on restart. Move to EF Core persistence early -- this is a correctness issue, not just a scale issue.
3. **Third bottleneck: Config reload.** JSON files require restart to pick up changes. Implement `IOptionsMonitor<T>` pattern or file system watchers for hot-reload.

## Anti-Patterns

### Anti-Pattern 1: Fat Backends

**What people do:** Put business logic (policy checks, sanitization, placement) inside individual backend implementations.
**Why it is wrong:** Every new backend duplicates the same logic. Policy enforcement becomes inconsistent. Testing requires spinning up real backends.
**Do this instead:** Keep backends thin -- they only know how to start/stop/interact with their execution environment. All cross-cutting concerns (policy, sanitization, audit) live in the SessionCoordinator, which wraps all backends uniformly. The scaffold already does this correctly.

### Anti-Pattern 2: Conflating Session State with Event Stream

**What people do:** Store session status by replaying the entire event stream. Or worse, store events in the session table.
**Why it is wrong:** Event replay is expensive for status checks. Session state and event streams have different lifecycles, retention policies, and query patterns.
**Do this instead:** Maintain a `Sessions` table with current state (updated on state transitions). Maintain a separate `SessionEvents` table or append-only log for the event stream. Project state changes into the session record when events occur.

### Anti-Pattern 3: Synchronous Backend Calls from API Endpoints

**What people do:** Block the API thread waiting for the backend to finish starting a session or executing a command on a remote host.
**Why it is wrong:** Remote operations (SSH connections, Nomad job submissions) can take seconds to minutes. Holding HTTP connections open for that long is fragile and limits concurrency.
**Do this instead:** Return immediately with a session ID after the backend acknowledges the request. Deliver results via the SSE event stream. The scaffold's `emit` callback pattern supports this but the backends need to actually run work asynchronously (e.g., background tasks for long-running SSH commands).

### Anti-Pattern 4: Monolithic Agent Adapter

**What people do:** Build one giant adapter class that handles Claude, Copilot, Codex, Gemini, and every other agent CLI with if/else branching.
**Why it is wrong:** Each agent CLI has different invocation patterns, output formats, and lifecycle management. A monolithic adapter becomes untestable.
**Do this instead:** Define an `IAgentAdapter` interface that each agent type implements. The adapter translates between the generic session model and the specific CLI invocation. Register adapters in DI and resolve by agent type. This is a layer below `ISessionBackend` -- a backend uses an adapter to actually run the agent process.

## Integration Points

### External Services

| Service | Integration Pattern | Notes |
|---------|---------------------|-------|
| Agent CLIs (claude, codex, gh copilot) | Process.Start with stdout/stderr capture | Each agent has different CLI flags, output formats. Wrap in `IAgentAdapter` per type. |
| Git repositories | `git worktree add` / `git clone --sparse` via CLI | GitWorktreeProvider shells out to git. Ensure git is installed on target hosts. |
| Blob storage | Azure Blob SDK or local filesystem abstraction | Start with local filesystem; swap to Azure Blob when cross-host sharing is needed. |
| Nomad cluster | Nomad HTTP API (jobs, allocations, logs) | REST calls to Nomad server. Use `HttpClient` with Aspire service discovery. |
| SSH hosts | SSH.NET library or Process.Start with ssh CLI | SSH.NET (Renci.SshNet) for programmatic control; fallback to CLI ssh for simplicity. |

### Internal Boundaries

| Boundary | Communication | Notes |
|----------|---------------|-------|
| API <-> Orchestration | Direct DI (in-process) | API layer is a thin shell over Orchestration services |
| Orchestration <-> Data | Interface abstraction (repository pattern) | Orchestration defines interfaces; Data project implements them |
| Coordinator <-> Backends | `ISessionBackend` interface | Backends are registered as `IEnumerable<ISessionBackend>` in DI |
| Coordinator <-> Policy/Sanitization | Synchronous in-process calls | These are fast, CPU-only checks; no need for async or out-of-process |
| Service <-> Clients | HTTP REST + SSE | Standard web API; SSE for real-time events |
| Service <-> AppHost | Aspire service discovery | AppHost declares resources; Service discovers via env vars |

## Build Order Implications

The architecture has clear dependency layers that dictate build order:

### Phase 1: Foundation (must come first)

1. **Contracts** -- shared types used everywhere. Already exists but needs review.
2. **ServiceDefaults** -- Aspire configuration shared across services. Create early.
3. **AppHost wiring** -- Register AgentHub.Service as an Aspire resource. Currently a stub.
4. **Data layer** -- EF Core DbContext, entities, initial migration. Does not exist yet. Critical for durable session state.

### Phase 2: Core Orchestration (depends on Phase 1)

5. **Session Coordinator** -- Already scaffolded. Needs real persistence integration instead of in-memory state in backends.
6. **Event Bus** -- Already scaffolded in-memory. Needs reconnection support (Last-Event-ID). Durable bus (Redis) can wait.
7. **One real backend** -- Pick SSH first since the target hosts already exist and have agent CLIs installed. InMemory stays for testing.

### Phase 3: Agent Execution (depends on Phase 2)

8. **Agent Adapters** -- New abstraction layer. `IAgentAdapter` per agent type (start with Claude CLI). This is what actually runs `claude` or `codex` on the remote host.
9. **Process management** -- Capture stdout/stderr from agent CLI processes, convert to SessionEvent stream. This is the core "see what every agent is doing" capability.

### Phase 4: Client Interfaces (depends on Phase 2)

10. **CLI client** -- First consumer of the API. Can be built as soon as Session CRUD + SSE endpoints work.
11. **Blazor dashboard** -- Second consumer. Needs session list, detail view, live event stream display.

### Phase 5: Advanced Features (depends on Phases 3-4)

12. **Placement engine improvements** -- Score-based selection, load awareness, affinity rules.
13. **Nomad backend** -- Requires a Nomad cluster. Build after SSH backend proves the model works.
14. **Approval flows** -- Human-in-the-loop for elevated/risky operations.
15. **Resource monitoring** -- CPU/memory/token-spend visibility from remote hosts.

### Dependency Graph

```
Contracts ─────────────┐
                       ▼
ServiceDefaults ──► AppHost
                       │
Data ◄── Orchestration ┤
              │        │
              ▼        ▼
         SSH Backend   API/SSE Endpoints
              │              │
              ▼              ▼
      Agent Adapters    CLI Client
      (Claude, etc.)    Blazor Dashboard
              │
              ▼
      Process Mgmt
      (stdout capture)
```

## Sources

- [AI Agent Orchestration Patterns - Azure Architecture Center](https://learn.microsoft.com/en-us/azure/architecture/ai-ml/guide/ai-agent-design-patterns) -- Microsoft's canonical reference for supervisor, sequential, and parallel agent orchestration patterns (HIGH confidence)
- [Server-Sent Events in ASP.NET Core and .NET 10](https://www.milanjovanovic.tech/blog/server-sent-events-in-aspnetcore-and-dotnet-10) -- TypedResults.ServerSentEvents pattern with IAsyncEnumerable (HIGH confidence)
- [.NET Aspire Service Discovery](https://learn.microsoft.com/en-us/dotnet/aspire/service-discovery/overview) -- Aspire service discovery via environment variables (HIGH confidence)
- [Control Plane as a Tool: A Scalable Design Pattern for Agentic AI Systems](https://arxiv.org/html/2505.06817) -- Academic paper on control plane architecture for multi-agent systems (MEDIUM confidence)
- [Orchestrating AI Agents in Production: The Patterns That Actually Work](https://hatchworks.com/blog/ai-agents/orchestrating-ai-agents/) -- Practical patterns for production agent orchestration (MEDIUM confidence)
- [Multi-Agent AI Orchestration: Enterprise Strategy for 2025-2026](https://www.onabout.ai/p/mastering-multi-agent-orchestration-architectures-patterns-roi-benchmarks-for-2025-2026) -- Hub-and-spoke vs mesh vs hybrid architecture comparison (MEDIUM confidence)
- Existing scaffold code in `src/AgentHub.Orchestration/` -- Direct analysis of current abstractions and patterns (HIGH confidence)

---
*Architecture research for: AgentSafeEnv multi-agent orchestration platform*
*Researched: 2026-03-08*
