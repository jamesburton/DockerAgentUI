# Project Research Summary

**Project:** AgentSafeEnv v1.1 -- Multi-Agent & Interactive
**Domain:** Multi-agent orchestration platform (extending shipped v1.0 with interactive steering, coordination, inventory, worktree isolation)
**Researched:** 2026-03-09
**Confidence:** HIGH

## Executive Summary

AgentSafeEnv v1.1 extends a stable, shipped v1.0 platform (12,000 LOC, 178 files, .NET 10) with four feature areas: interactive session steering (pause/resume/redirect), multi-agent coordination (sub-agent spawning, resource-aware scheduling), host inventory discovery, and git worktree isolation for parallel agents. The strongest finding across all research is that **zero new dependencies are required** -- every v1.1 feature is achievable with the existing stack (SSH.NET, EF Core SQLite, System.Threading.Channels, SSE) plus Claude Code CLI flags verified against official documentation. This zero-dependency position eliminates version conflict risk and keeps the learning curve flat.

The recommended approach is a dependency-driven build order: host inventory first (everything else depends on knowing what is installed where), then interactive steering and worktree isolation in parallel (independent of each other), then multi-agent coordination last (depends on all three predecessors). Architecture research confirms that the v1.0 abstractions -- `ISessionBackend`, `IPlacementEngine`, `DurableEventService`, `HostCommandProtocol` -- accommodate all four features through extension rather than replacement. The only component requiring a full rewrite is `GitWorktreeProvider`, which is currently a stub.

The critical risks are: (1) state machine explosion when adding pause/resume semantics -- the coordinator and host daemon can diverge under network partitions, producing phantom "Paused" states; (2) unbounded sub-agent spawning cascading into runaway resource consumption (Anthropic's own multi-agent system documented 50-subagent cascades for simple queries); and (3) git worktree shared `.git` directory corruption when multiple agents operate concurrently on the same repository. All three are preventable with specific design constraints: formal state transition diagrams with daemon-authoritative state, hard depth/count limits on spawning, and per-repo mutex serialization for worktree operations.

## Key Findings

### Recommended Stack

No new NuGet packages. The v1.1 feature set maps entirely to existing primitives and Claude Code CLI flags. See [STACK.md](STACK.md) for the full integration matrix.

**Core technologies (unchanged):**
- **.NET 10 / ASP.NET Core / EF Core SQLite** -- runtime, API, persistence. LTS, proven in v1.0
- **SSH.NET 2025.1.0** -- bidirectional remote execution. stdin pipe handles steering commands; stdout/stderr for events
- **System.Threading.Channels (BCL)** -- in-process async queues for event routing, already used in DurableEventService
- **SSE (built-in)** -- server-to-client streaming. No SignalR needed; SSE + SSH stdin already provides bidirectional communication

**New internal abstractions (no packages):**
- `ICoordinationService` -- multi-agent dispatch and sub-agent spawning
- `HostInventoryService` -- periodic SSH-based agent CLI discovery
- `HostMetricCache` -- in-memory ConcurrentDictionary bridging metric polling to placement engine
- `ResourceAwarePlacementEngine` -- weighted scoring replacement for `SimplePlacementEngine.FirstOrDefault()`

**Explicitly rejected:** SignalR, RabbitMQ/Redis, gRPC, Hangfire, MediatR, Polly, Semantic Kernel, CliWrap, MCP SDK. Each has a documented rationale in STACK.md.

### Expected Features

See [FEATURES.md](FEATURES.md) for full prioritization matrix and dependency graph.

**Must have (table stakes):**
- Session pause/resume via SIGTSTP/SIGCONT over SSH
- Git worktree creation and cleanup per session
- Host tool inventory -- know what agent CLIs are installed before dispatching
- Agent CLI version detection -- prevent incompatible flag dispatch
- Resource-aware placement -- stop stacking agents on one host while others sit idle
- Follow-up instruction UX -- polish existing `SendInputAsync` for mid-session steering

**Should have (differentiators):**
- Sub-agent spawning with parent-child tracking (no self-hosted tool does cross-machine spawning)
- Coordinated multi-session batch launch
- Resource-aware auto-scheduling with weighted scoring
- Host capability fingerprinting (agent CLIs, git version, disk space, available tools)
- Automatic worktree branch naming (`agent/{sessionId}/{prompt-prefix}`)

**Defer to v1.2+:**
- Session dependency DAG execution (fan-out/fan-in)
- Prompt redirect with idle detection (fragile pattern matching)
- Dedicated health-check probes (host metrics polling suffices at current scale)
- Worktree merge-readiness check (easy add-on after worktree lifecycle is proven)

### Architecture Approach

The v1.0 architecture accommodates v1.1 through extension, not restructuring. The coordinator remains stateless with respect to repository data. The `HostCommandProtocol` gains 4 new commands. The `DurableEventService` and `SseSubscriptionManager` require no changes. See [ARCHITECTURE.md](ARCHITECTURE.md) for integration points and data flows.

**Major components:**
1. **SessionCoordinator** (modified) -- gains control signal routing (pause/resume/redirect) in `SendInputAsync`
2. **CoordinationService** (new) -- orchestrates multi-session dispatch and sub-agent spawning via existing `SessionCoordinator`
3. **ResourceAwarePlacementEngine** (replaces SimplePlacementEngine) -- weighted scoring of CPU/memory/session-count with pending-session counter
4. **HostInventoryService** (new) -- background SSH discovery of agent CLIs, versions, disk space, git version
5. **GitWorktreeProvider** (rewritten) -- stub replaced with real SSH-based `git worktree add/remove` operations
6. **HostMetricCache** (new) -- ConcurrentDictionary singleton bridging polling service to placement engine

### Critical Pitfalls

See [PITFALLS.md](PITFALLS.md) for all 7 pitfalls with recovery strategies.

1. **State machine explosion (interactive steering)** -- Adding pause/resume creates implicit substates that interact with the existing lifecycle. Draw the complete state transition diagram before writing code. Separate control plane from data plane. Host daemon owns authoritative state; coordinator DB is a cache.
2. **Unbounded sub-agent spawning** -- Without depth limits and fleet-wide session caps, a single dispatch can cascade into 50+ agents. Enforce hard limits: max depth (2), max children per parent (10), max fleet-wide sessions (20). Implement token/cost budget inheritance.
3. **Git shared .git directory corruption** -- Concurrent worktree operations contend on `.git/index.lock`. Serialize all `git worktree add/remove` per-repo with a mutex in the host daemon. Disable `gc.auto`. Use `--no-optional-locks` for read operations.
4. **Coordinator-daemon state divergence** -- SSH command delivery is at-most-once. Implement command acknowledgment with unique IDs. Daemon state wins on conflict. Add periodic state reconciliation.
5. **Thundering herd placement** -- Stale 30-second metrics cause burst dispatches to pick the same "best" host. Add pending-session counter that increments at placement time, jitter among equal hosts, and admission control per host.

## Implications for Roadmap

Based on combined research, the dependency graph dictates a 5-phase structure. Host inventory is the foundation; multi-agent coordination is the capstone.

### Phase 1: Schema and Shared Infrastructure
**Rationale:** Every feature needs schema changes and the HostMetricCache. Doing this first avoids migration conflicts and provides the data layer all subsequent phases depend on.
**Delivers:** EF Core migration (ParentSessionId, DispatchId, InventoryJson, Paused state), HostMetricCache singleton, contract DTOs (SessionControlSignal, DispatchRequest, HostInventory, AgentCliInfo).
**Addresses:** Foundation for all table-stakes features.
**Avoids:** Migration conflicts from parallel development; stale NodeCapability fields (CpuTotal/MemTotalMb hardcoded to 0).

### Phase 2: Host Inventory Discovery
**Rationale:** Almost every v1.1 feature depends on knowing what is installed where. Worktrees need git version verification. Placement needs tool availability. Sub-agent spawning needs target host validation.
**Delivers:** HostInventoryService (background polling), HostCommandProtocol `discover-inventory` command, API endpoints for inventory query and on-demand refresh, disk space in fast health poll.
**Addresses:** Host tool inventory, agent CLI version detection, host capability fingerprinting.
**Avoids:** Pitfall 4 (stale inventory causing placement failures) -- separate slow discovery (5-10 min) from fast health (30s), include disk space in fast poll.

### Phase 3: Interactive Session Steering
**Rationale:** Independent of coordination features. Simpler to test on single sessions before orchestrating multiples. Must establish the state machine pattern that coordination will inherit.
**Delivers:** Pause/resume via SSH signals, SessionControlSignal routing in SessionCoordinator, HostCommandProtocol pause/resume commands, command acknowledgment protocol.
**Addresses:** Session pause/resume, follow-up instruction UX, prompt redirect foundation.
**Avoids:** Pitfall 1 (state machine explosion) -- formal transition diagram required; Pitfall 5 (coordinator-daemon divergence) -- ack protocol implemented here.

### Phase 4: Git Worktree Isolation
**Rationale:** Must be working before multi-agent dispatch to prevent merge conflict chaos. Depends on Phase 2 inventory for git version verification.
**Delivers:** GitWorktreeProvider rewrite (real SSH-based git worktree operations), SshBackend integration for session start/stop, worktree cleanup lifecycle, automatic branch naming.
**Addresses:** Git worktree creation/cleanup per session, worktree branch naming.
**Avoids:** Pitfall 3 (shared .git corruption) -- per-repo mutex, gc.auto=0; Pitfall 6 (merge conflict avalanche) -- file ownership hints in data model, conflict pre-check.

### Phase 5: Multi-Agent Coordination
**Rationale:** Most complex feature. Depends on inventory (host validation), steering (state machine), worktrees (isolation), and placement (resource awareness). Building last means all prerequisites are proven.
**Delivers:** CoordinationService, ResourceAwarePlacementEngine, sub-agent spawning API, batch dispatch endpoint, parent-child session tracking, event fan-out to parent streams.
**Addresses:** Sub-agent spawning, coordinated multi-session launch, resource-aware auto-scheduling, session parent-child tracking.
**Avoids:** Pitfall 2 (unbounded spawning) -- depth/count limits from day one; Pitfall 7 (thundering herd) -- pending-session counter and admission control.

### Phase Ordering Rationale

- **Inventory before everything** because placement, worktrees, and coordination all need to know what is installed on each host. Building it first means every subsequent phase has accurate host data.
- **Steering before coordination** because pause/resume semantics must work reliably on single sessions before orchestrating parent-child relationships. The command ack protocol designed here is reused by coordination.
- **Worktrees before coordination** because dispatching parallel agents to the same repo without worktree isolation will produce merge conflicts immediately. Having isolation ready means the first multi-agent dispatch works safely.
- **Coordination last** because it is a thin orchestration layer over `SessionCoordinator` -- it reuses the full session lifecycle for each child session. With all prerequisites solid, coordination is primarily composition, not invention.

### Research Flags

Phases likely needing deeper research during planning:
- **Phase 3 (Interactive Steering):** The SIGTSTP/SIGCONT signal approach needs validation -- not all agent CLIs respond to process suspension. Fallback semantics (buffer input vs. actually freeze the process) need design. Command ack protocol is novel for this codebase.
- **Phase 5 (Multi-Agent Coordination):** Sub-agent event fan-out to parent streams, cascade stop/orphan policy for child sessions, and cost budget inheritance are all design-heavy. Claude Code's `--agents` flag and `--teammate-mode` need integration testing.

Phases with standard patterns (skip research-phase):
- **Phase 1 (Schema/Infrastructure):** Standard EF Core migration and ConcurrentDictionary cache. Well-trodden ground.
- **Phase 2 (Host Inventory):** Follows exact same pattern as existing `HostMetricPollingService` -- BackgroundService + SSH + DB persistence. Copy the pattern.
- **Phase 4 (Git Worktrees):** `git worktree add/remove` is well-documented. The Claude Code `--worktree` flag is verified. Main novelty is the per-repo mutex in the host daemon.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | Zero new dependencies. All Claude Code CLI flags verified against official docs (2026-03-09). Existing codebase is primary source. |
| Features | MEDIUM-HIGH | Feature landscape well-mapped. Dependency graph clear. Some differentiators (prompt redirect with idle detection) have uncertain feasibility, correctly deferred. |
| Architecture | HIGH | Based on direct analysis of shipped 12,000 LOC codebase. Integration points mapped to specific files and methods. All modifications are additive except GitWorktreeProvider rewrite. |
| Pitfalls | HIGH | Critical pitfalls corroborated across Anthropic engineering blog, git worktree community reports, and existing codebase analysis. Recovery strategies documented. |

**Overall confidence:** HIGH

### Gaps to Address

- **SIGTSTP behavior across agent CLIs:** Only Claude Code's response to SIGTSTP has been partially documented. If agents ignore the signal, the "Paused" state becomes misleading. Needs testing during Phase 3. Fallback: treat pause as "stop reading stdin" rather than process suspension.
- **Claude Code `--agents` flag in headless mode:** The `--agents` JSON configuration is documented but untested in SSH-launched headless sessions. Verify during Phase 5 that `--agents` works with `--output-format stream-json`.
- **Worktree disk space at scale:** Each worktree consumes disk. With 10+ concurrent sessions on one host, disk pressure becomes a placement constraint. Phase 2 adds disk monitoring, but threshold tuning needs empirical data.
- **Event fan-out for deep sub-agent trees:** Parent SSE stream receiving events from 5+ verbose children may overwhelm clients. Aggregation strategy (summary events vs. filtered forwarding) needs design during Phase 5.
- **Worktree strategy reconciliation:** FEATURES.md recommends orchestrator-managed worktrees (agent-agnostic, `git worktree add` via SSH). STACK.md recommends passing `--worktree` to Claude Code. Resolution: use orchestrator-managed approach for agent-agnostic support, support `--worktree` passthrough as optimization for Claude Code sessions. Design `GitWorktreeProvider` to accommodate both.

## Sources

### Primary (HIGH confidence)
- [Claude Code CLI Reference](https://code.claude.com/docs/en/cli-reference) -- all CLI flags verified (session, worktree, agents, budget, model)
- [Claude Code Sub-agents](https://code.claude.com/docs/en/sub-agents) -- sub-agent definition and coordination
- [Claude Code Agent Teams](https://code.claude.com/docs/en/agent-teams) -- experimental multi-agent teams
- [Claude Code Common Workflows](https://code.claude.com/docs/en/common-workflows) -- worktree workflow patterns
- Existing codebase analysis (SessionCoordinator, SshBackend, DurableEventService, HostCommandProtocol, ClaudeCodeAdapter, GitWorktreeProvider, SimplePlacementEngine, HostMetricPollingService) -- 12,000 LOC, 178 files

### Secondary (MEDIUM-HIGH confidence)
- [How we built our multi-agent research system](https://www.anthropic.com/engineering/multi-agent-research-system) -- Anthropic Engineering Blog (sub-agent limits, observability)
- [Git worktrees for parallel AI coding agents](https://devcenter.upsun.com/posts/git-worktrees-for-parallel-ai-coding-agents/) -- Upsun (port conflicts, disk, merge conflicts)
- [AI Agent Orchestration Patterns](https://learn.microsoft.com/en-us/azure/architecture/ai-ml/guide/ai-agent-design-patterns) -- Microsoft (orchestrator-worker patterns)
- [Git Worktrees: The Secret Weapon for Parallel AI Coding Agents](https://medium.com/@mabd.dev/git-worktrees-the-secret-weapon-for-running-multiple-ai-coding-agents-in-parallel-e9046451eb96)

### Tertiary (MEDIUM confidence)
- [Multi-Agent Coordination Strategies](https://galileo.ai/blog/multi-agent-coordination-strategies) -- Galileo AI (task ownership, cascading failures)
- [Multi-Agent Orchestration Patterns 2025-2026](https://www.onabout.ai/p/mastering-multi-agent-orchestration-architectures-patterns-roi-benchmarks-for-2025-2026)
- [How to Build Multi-Agent Systems: Complete 2026 Guide](https://dev.to/eira-wexford/how-to-build-multi-agent-systems-complete-2026-guide-1io6)

---
*Research completed: 2026-03-09*
*Ready for roadmap: yes*
