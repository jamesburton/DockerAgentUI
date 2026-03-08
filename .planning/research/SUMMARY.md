# Project Research Summary

**Project:** AgentSafeEnv - Multi-Agent Orchestration Platform
**Domain:** Multi-agent coding orchestration control plane (fleet management for AI coding agents across machines)
**Researched:** 2026-03-08
**Confidence:** HIGH

## Executive Summary

AgentSafeEnv is a self-hosted, multi-machine orchestration platform for AI coding agents (Claude Code, Codex, Copilot, Gemini, OpenCode). This is a control plane, not an AI agent itself -- it launches, monitors, and manages CLI-based coding agents across 5-10 remote machines via a hub-and-spoke architecture. The competitive gap it fills is clear: local tools like Claude Squad and Conductor are single-machine only, while cloud platforms like Warp Oz and Devin are SaaS. Nobody offers a self-hosted fleet orchestrator for multiple agent types with pluggable execution backends.

The recommended approach is a .NET 10 + Aspire stack with ASP.NET Core as the coordinator API, native SSE for real-time event streaming, EF Core with SQLite for persistence, and SSH as the first execution backend. The architecture follows a supervisor pattern: a central coordinator receives all requests, applies policy and sanitization checks, dispatches work to backends (SSH, then Nomad), and streams events back to clients (CLI first, Blazor dashboard second). Agent CLI differences are handled by an adapter-per-agent-type pattern, starting with Claude Code only in v1.

The top risks are: (1) agent CLI instability -- these tools change output formats frequently, requiring version-aware adapters from day one; (2) remote process lifecycle management -- SSH sessions must not tie process lifetime to connection lifetime, requiring a host-side daemon; (3) SSE event gaps causing silent data loss on reconnection -- events need IDs and replay from the start; (4) the coordinator's in-memory session state being lost on restart -- persistent storage must replace ConcurrentDictionary before any real deployment. These are all solvable with the right Phase 1 foundations.

## Key Findings

### Recommended Stack

The stack is entirely .NET 10 ecosystem, which is the right call for a C#-first team wanting full-stack unity from CLI to web dashboard to eventual desktop app. All core technologies are LTS or GA with high confidence in version stability.

**Core technologies:**
- **.NET 10 / ASP.NET Core 10:** Runtime and API host -- LTS until Nov 2028, native SSE support via `Results.ServerSentEvents`
- **Aspire 13.x:** Dev orchestration -- service discovery, OpenTelemetry dashboard, integration testing out of the box
- **EF Core 10 + SQLite:** Data persistence -- zero-config for MVP, swappable to Postgres/SQL Server via provider abstraction
- **System.CommandLine 2.0 + Spectre.Console:** CLI interface -- finally stable (Nov 2025), paired with rich terminal rendering
- **CliWrap 3.10:** Process management -- async stream capture of agent CLI stdout/stderr
- **SSH.NET 2025.1.0:** Remote execution -- mature SSH-2 library for the SSH backend
- **ModelContextProtocol 1.1.0:** MCP support -- official Microsoft/Anthropic C# SDK for future protocol bridge
- **Blazor Interactive Server:** Web dashboard -- server-rendered for real-time updates, reusable in MAUI Hybrid later
- **Nomad REST API via HttpClient:** No .NET SDK exists; thin wrapper is the correct approach

**Key "do not use" decisions:** Semantic Kernel (wrong abstraction -- this manages CLI tools, not LLM agents), SignalR initially (SSE sufficient for monitoring), Newtonsoft.Json (legacy), Docker SDK (out of scope per PROJECT.md).

### Expected Features

**Must have (table stakes -- v1):**
- Host registration with agent tool discovery
- Host agent daemon for remote process management
- Session launch via SSH to registered hosts
- Agent CLI adapters (Claude Code + one other)
- Git worktree isolation per session
- Real-time output streaming via SSE
- Session lifecycle management (start/stop/status/force-kill)
- Session history with stored output
- CLI client as primary interface
- Coordinator REST API

**Should have (differentiators -- v1.x):**
- Multi-machine fleet management (core differentiator vs. competitors)
- Pluggable execution backends (SSH first, Nomad second)
- Diff review workflow
- Policy/skill configuration (YAML-based)
- Approval/elevation flow (human-in-the-loop)
- Resource usage visibility
- Web dashboard (Blazor)
- Input sanitization layer

**Defer (v2+):**
- Interactive bidirectional sessions
- Nomad and container execution backends
- Cross-machine task coordination
- MAUI desktop/mobile client
- Token spend tracking
- MCP protocol support for agent control

**Anti-features to actively avoid:** Full container sandboxing from day one, agent-to-agent direct communication, auto-merge of agent PRs, natural language task decomposition, multi-tenant auth.

### Architecture Approach

Hub-and-spoke supervisor pattern with a central stateless coordinator, pluggable execution backends behind `ISessionBackend`, and an event-sourcing-lite model where SSE streams are the primary output channel. Three-tier state separation keeps system config, task state, and ephemeral session context cleanly isolated.

**Major components:**
1. **Coordinator API** -- ASP.NET Core minimal APIs; routes requests, applies policy, dispatches to backends
2. **Session Coordinator** -- Orchestrates placement, policy check, sanitization, and backend routing (stateless, DB-backed)
3. **Execution Backends** -- `ISessionBackend` implementations: InMemory (dev), SSH (v1), Nomad (v2+)
4. **Agent Adapters** -- `IAgentAdapter` per agent type; translates between generic session model and specific CLI invocation
5. **Event Bus** -- In-process `Channel<T>` for fan-out to SSE subscribers; Redis/NATS later for durability
6. **Data Layer** -- EF Core with SQLite for session state, host records, audit trail
7. **Config Layer** -- JSON-based skills, policies, host registry; DB-backed later
8. **Aspire AppHost** -- Local dev orchestration, service discovery, telemetry

**Project structure:** 9 projects -- AppHost, Contracts, Orchestration (no web dependency), Data (EF Core), Service (thin API host), CLI, Web (Blazor), ServiceDefaults, and future MAUI.

### Critical Pitfalls

1. **Agent CLI instability** -- CLIs change output formats without warning. Build version-aware adapters per agent type; use `--json` flags exclusively; never parse human-readable stdout.
2. **Remote process lifecycle loss** -- SSH disconnection kills agent processes; coordinator restart loses session tracking. Launch agents in tmux/nohup; deploy a host-side daemon; persist session state to DB, not ConcurrentDictionary.
3. **SSE event gaps** -- Client disconnects lose events silently. Assign monotonic IDs to all events; buffer recent events; support `Last-Event-ID` replay on reconnect.
4. **Git worktree contention** -- Concurrent agents on same repo hit branch locks and disk exhaustion. Use unique branch names per worktree; implement cleanup lifecycle; enforce per-host limits.
5. **Blacklist sanitization creates false security** -- Regex blocklists are trivially bypassable. Treat sanitization as advisory only; real security boundary is execution environment isolation (containers, restricted users).
6. **Coordinator as single point of failure** -- In-memory state means restart = total state loss. Make coordinator stateless from day one with DB-backed session state.
7. **Agent behavior differences hidden behind uniform interface** -- Each agent CLI has fundamentally different interaction models. Build and ship one agent at a time; document per-agent capability matrices.

## Implications for Roadmap

Based on research, suggested phase structure:

### Phase 1: Foundation and Data Layer
**Rationale:** Architecture research shows clear dependency: Contracts and Data must exist before any orchestration logic can be durable. The scaffold has in-memory state that must be replaced before any real execution. This is the foundation everything else builds on.
**Delivers:** Project structure, shared contracts, EF Core DbContext with entities and migrations, Aspire AppHost wiring, ServiceDefaults with telemetry/health.
**Addresses:** Host registration data model, session persistence, audit trail foundation.
**Avoids:** Pitfall 2 (process lifecycle loss from in-memory state), Pitfall 6 (coordinator SPOF).

### Phase 2: Core Orchestration and Event Streaming
**Rationale:** With persistent storage in place, the coordinator can manage sessions durably. Event streaming with IDs and replay must be built correctly from the start per Pitfall 3.
**Delivers:** Session Coordinator with DB-backed state, event bus with ID assignment and `Last-Event-ID` replay, SSE endpoints using .NET 10 native `TypedResults.ServerSentEvents`, placement engine (simple filter-based), policy and sanitization pipeline (advisory).
**Addresses:** Session lifecycle management, real-time output streaming, host inventory.
**Avoids:** Pitfall 3 (SSE event gaps), Pitfall 5 (false security from sanitizer -- label it advisory).

### Phase 3: SSH Backend and First Agent Adapter
**Rationale:** This is where the platform starts actually doing things. SSH is the simplest real backend and the target hosts already exist. Claude Code is the best-documented agent with JSON output support. One real backend + one real agent proves the entire pipeline end-to-end.
**Delivers:** SSH execution backend, host-side process management (tmux/nohup), Claude Code agent adapter with version detection, git worktree creation per session.
**Addresses:** Session launch on remote hosts, agent CLI execution, worktree isolation.
**Avoids:** Pitfall 1 (CLI API instability -- version-aware adapter), Pitfall 2 (process lifecycle -- tmux/daemon), Pitfall 7 (agent differences -- start with one agent only).

### Phase 4: CLI Client
**Rationale:** The CLI is the primary interface for the target user (power users, developers). It can be built as soon as Session CRUD + SSE endpoints work. Building the CLI forces validation of the API surface.
**Delivers:** Full CLI client with session management (create, list, stop, logs), live event streaming display, host management commands.
**Uses:** System.CommandLine 2.0 for parsing, Spectre.Console for rich output.
**Addresses:** CLI as primary interface, session history viewing.

### Phase 5: Second Agent, Diff Review, and Policies
**Rationale:** With one agent working end-to-end, add a second agent to validate the adapter abstraction. Diff review and policy configuration make the tool production-quality for daily use.
**Delivers:** Second agent adapter (Codex or OpenCode), diff review workflow, YAML-based policy/skill configuration, approval/elevation flow for gated actions.
**Addresses:** Multi-agent-type support, diff review, config-driven policies, human-in-the-loop.
**Avoids:** Pitfall 7 (agent differences -- second agent validates the abstraction holds).

### Phase 6: Web Dashboard
**Rationale:** With a stable API and CLI proving the model works, the Blazor dashboard provides visual fleet overview. Same API, second UI surface.
**Delivers:** Blazor Interactive Server dashboard with session list, host status, live event streaming display, diff viewer.
**Uses:** Blazor Interactive Server, SSE for real-time updates.
**Addresses:** Web dashboard, fleet status visibility, resource usage display.

### Phase 7: Advanced Backends and Hardening
**Rationale:** With core orchestration proven, add Nomad for better scheduling/isolation, resource monitoring, notification system, and additional agent adapters.
**Delivers:** Nomad execution backend, resource usage monitoring, notification system, additional agent adapters.
**Addresses:** Pluggable execution backends, resource visibility, notifications.

### Phase Ordering Rationale

- **Data before orchestration:** The architecture research explicitly warns that in-memory session state is the most critical gap in the existing scaffold. Fixing this first prevents every subsequent phase from building on a broken foundation.
- **Event model before backends:** SSE event IDs and replay must be in the data model before any real events flow through the system. Retrofitting IDs is painful.
- **SSH before Nomad:** SSH is simpler, target hosts already exist, and it validates the backend abstraction without requiring cluster infrastructure.
- **One agent before many:** Every pitfall and architecture document emphasizes building and testing one agent adapter completely before adding others. Claude Code first (best docs, JSON output).
- **CLI before web dashboard:** CLI is the primary interface and forces clean API design. Dashboard is the same API with a visual layer on top.
- **Policies and approval after basic orchestration works:** Policy enforcement is meaningless without working session execution to gate.

### Research Flags

Phases likely needing deeper research during planning:
- **Phase 3 (SSH Backend + Agent Adapter):** Complex integration -- SSH process lifecycle management, tmux session handling, Claude Code CLI flags and output parsing all need specific API research.
- **Phase 5 (Second Agent + Policies):** The second agent adapter will surface abstraction gaps. Codex CLI has a different execution model (sandbox-first). Policy YAML schema design needs research.
- **Phase 7 (Nomad Backend):** Nomad REST API integration, job spec templates, log streaming from allocations -- all need dedicated research.

Phases with standard patterns (skip research-phase):
- **Phase 1 (Foundation):** Standard EF Core setup, Aspire wiring -- well-documented Microsoft patterns.
- **Phase 2 (Core Orchestration):** SSE in .NET 10 is well-documented. Channel-based event bus is standard.
- **Phase 4 (CLI Client):** System.CommandLine 2.0 and Spectre.Console are well-documented.
- **Phase 6 (Web Dashboard):** Blazor Interactive Server is well-documented. Standard CRUD + SSE consumption.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | All technologies verified on NuGet with specific versions. .NET 10 LTS, Aspire 13.x GA, System.CommandLine finally stable. No speculative picks. |
| Features | HIGH | Feature landscape validated against 5+ competitors (Claude Squad, Conductor, Warp Oz, GitHub Mission Control, Devin). Clear competitive positioning. MVP scope well-defined. |
| Architecture | HIGH | Hub-and-spoke supervisor pattern is the industry standard for fleet management. Verified against Microsoft Azure Architecture Center patterns and academic papers. Existing scaffold validates the component structure. |
| Pitfalls | HIGH | Pitfalls corroborated across GitHub Engineering Blog, Microsoft docs, and community experience. Each pitfall maps to specific scaffold code that exhibits the risk. |

**Overall confidence:** HIGH

### Gaps to Address

- **Host daemon design:** Research identifies the need for a lightweight host-side daemon but does not specify its protocol, deployment model, or update mechanism. Needs design during Phase 3 planning.
- **Agent CLI version compatibility matrix:** Which versions of Claude Code, Codex, etc. are supported? No formal compatibility testing plan exists. Address during Phase 3 and Phase 5.
- **Nomad job specification templates:** The Nomad backend is deferred to Phase 7 but the job spec design (resource limits, networking, artifact injection) needs research before implementation.
- **SQLite concurrency limits:** Research flags SQLite concurrent write limitations. Need to determine the threshold where migration to Postgres becomes necessary (likely 10+ concurrent sessions).
- **MAUI Blazor Hybrid component reuse:** The strategy assumes Blazor components can be shared between web and MAUI. This needs validation during Phase 6 to ensure components are designed for reuse.
- **Token spend tracking:** Listed as v2+ feature but no research on how to extract token usage from different agent CLIs. Some agents expose this, others do not.

## Sources

### Primary (HIGH confidence)
- [.NET 10 What's New](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/overview)
- [ASP.NET Core 10 Release Notes](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0)
- [Aspire 13 What's New](https://aspire.dev/whats-new/aspire-13/)
- [EF Core 10 What's New](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew)
- [SSE in ASP.NET Core .NET 10](https://www.milanjovanovic.tech/blog/server-sent-events-in-aspnetcore-and-dotnet-10)
- [ModelContextProtocol 1.0 Release](https://www.dotnetramblings.com/post/05_03_2026/05_03_2026_3/)
- [AI Agent Orchestration Patterns - Azure Architecture Center](https://learn.microsoft.com/en-us/azure/architecture/ai-ml/guide/ai-agent-design-patterns)
- [.NET Aspire Service Discovery](https://learn.microsoft.com/en-us/dotnet/aspire/service-discovery/overview)
- [Multi-agent workflows often fail - GitHub Engineering](https://github.blog/ai-and-ml/generative-ai/multi-agent-workflows-often-fail-heres-how-to-engineer-ones-that-dont/)

### Secondary (MEDIUM confidence)
- [Conductors to Orchestrators (Addy Osmani / O'Reilly)](https://www.oreilly.com/radar/conductors-to-orchestrators-the-future-of-agentic-coding/)
- [Control Plane as a Tool (arXiv)](https://arxiv.org/html/2505.06817)
- [Orchestrating AI Agents in Production (Hatchworks)](https://hatchworks.com/blog/ai-agents/orchestrating-ai-agents/)
- [10 Things Developers Want from Agentic IDEs (RedMonk)](https://redmonk.com/kholterhoff/2025/12/22/10-things-developers-want-from-their-agentic-ides-in-2025/)
- [Codex App Worktrees Explained](https://www.verdent.ai/guides/codex-app-worktrees-explained)

### Tertiary (LOW confidence)
- [Multi-Agent AI Orchestration: Enterprise Strategy 2025-2026 (OnAbout.ai)](https://www.onabout.ai/p/mastering-multi-agent-orchestration-architectures-patterns-roi-benchmarks-for-2025-2026)
- [Agent Orchestration is Governance (Medium)](https://medium.com/@markus_brinsa/agent-orchestration-orchestration-isnt-magic-it-s-governance-210afb343914)

---
*Research completed: 2026-03-08*
*Ready for roadmap: yes*
