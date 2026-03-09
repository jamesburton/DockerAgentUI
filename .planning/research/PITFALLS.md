# Pitfalls Research

**Domain:** Multi-agent orchestration platform -- v1.1 features (interactive steering, multi-agent coordination, host inventory, git worktree isolation)
**Researched:** 2026-03-09
**Confidence:** HIGH (corroborated across Anthropic engineering blog, git worktree community reports, and existing codebase analysis)

## Critical Pitfalls

### Pitfall 1: Interactive Steering State Machine Explosion

**What goes wrong:**
Adding pause/resume/redirect to running sessions creates an implicit state machine (Running, Paused, Redirecting, WaitingForInput, Resuming) that interacts with the existing session lifecycle (Pending, Running, Stopped, Failed). The combinatorial explosion of valid state transitions leads to impossible-to-reproduce bugs: a pause arrives while the agent is mid-tool-call, a redirect arrives during a pause, a stop arrives during a redirect. The `SessionState` enum grows but the code that checks it does not cover all transitions, producing inconsistent states between the coordinator's view and the host daemon's reality.

**Why it happens:**
The current `SessionState` enum has 4 values. Interactive steering implicitly adds 3-5 more substates. Developers add the new states incrementally ("just add Paused") without mapping the full transition graph. The `SshBackend.SendInputAsync` method currently has no concept of session state preconditions -- it just sends input if a connection exists. Adding steering commands through the same `SendInputRequest` path conflates data input with control signals.

**How to avoid:**
- Draw the complete state transition diagram BEFORE writing code. Every (currentState, incomingCommand) pair must have a defined outcome: succeed, reject, or queue.
- Separate the control plane (pause, resume, redirect, stop) from the data plane (send text input). These should be different API endpoints and different `ISessionBackend` methods, not overloaded through `SendInputRequest`.
- The host daemon must own the authoritative state. The coordinator's DB is a cache of what the daemon reported. Never let the coordinator assume a state transition succeeded without confirmation from the daemon.
- Add a `SessionSubState` or redesign `SessionState` as a proper state machine with transitions enforced in code (a `TransitionTo` method that throws on invalid transitions).

**Warning signs:**
- Bug reports involving "I paused but the agent kept running"
- State field in DB says "Paused" but the SSH connection is still receiving stdout
- Race conditions in tests that only reproduce under load

**Phase to address:**
Interactive Steering phase -- must be designed as a formal state machine before implementation begins. Retrofitting state transitions into the current simple enum will produce bugs that are expensive to diagnose.

---

### Pitfall 2: Sub-Agent Spawning Creates Unbounded Resource Consumption

**What goes wrong:**
The coordinator dispatches a task to Host A. The task is complex, so the orchestration logic spawns 3 sub-agents across Hosts A, B, and C. Each sub-agent can itself decide it needs help and request more sub-agents. Without depth limits and total session caps, a single user request cascades into 50+ agent processes across the fleet, each consuming API tokens (at approximately 15x the cost of a single chat interaction per Anthropic's findings). The operator discovers the runaway only when they get a surprise API bill or hosts become unresponsive.

**Why it happens:**
Anthropic's own multi-agent research system documented this exact failure: "Early versions created 50 subagents for simple queries, dramatically wasting tokens." The natural architecture -- "coordinator spawns sub-agents when resources are busy" -- has no built-in termination condition. The current `SimplePlacementEngine` picks the first eligible node with no concept of how many sessions already run there. The `SessionCoordinator.StartSessionAsync` has no recursion guard or fleet-wide session limit.

**How to avoid:**
- Implement hard limits: max sub-agent depth (e.g., 2 levels), max total sessions per parent task (e.g., 10), max concurrent sessions per host (e.g., 3), max concurrent sessions fleet-wide (e.g., 20).
- Store the parent-child relationship in the session entity. Every sub-agent session records its `ParentSessionId`. The coordinator checks the depth before spawning.
- Implement a token/cost budget per top-level task. Sub-agents inherit a fraction of the parent's remaining budget. When budget is exhausted, no more spawning.
- The `SimplePlacementEngine.ChooseNode` currently does `q.FirstOrDefault()` -- it must be upgraded to consider current load (active session count, CPU/memory utilization from `HostMetricPollingService`).
- Add a circuit breaker: if more than N sessions are spawned within T seconds from the same parent, halt and require operator confirmation.

**Warning signs:**
- Session count on a host exceeds the intended limit
- `HostMetricPollingService` shows CPU/memory saturated on multiple hosts simultaneously
- API token spend spikes correlate with multi-agent dispatches

**Phase to address:**
Multi-Agent Coordination phase -- the spawning limits and parent-child tracking must be implemented alongside the dispatch mechanism, not added after. This is the single highest-risk pitfall for v1.1.

---

### Pitfall 3: Git Worktree Shared `.git` Directory Creates Silent Corruption

**What goes wrong:**
Multiple agent sessions on the same host work on the same repository via git worktrees. Worktrees share the `.git` directory (objects, refs, index lock). When two agents run `git commit`, `git checkout`, or `git rebase` simultaneously, they contend on `.git/index.lock`. One agent's operation fails with a lock error. Worse: certain concurrent operations (packfile repacking, garbage collection triggered by thresholds) can corrupt shared objects, breaking ALL worktrees for that repo -- not just the one that triggered the corruption.

**Why it happens:**
The current `GitWorktreeProvider.EnsureMaterializedAsync` creates directories and writes a metadata file -- it does not actually run `git worktree add`. The real implementation will need to call `git worktree add` on the remote host, and that command takes a lock on the shared `.git` directory. Developers test with one worktree at a time and never see contention. Git's lock mechanism is advisory on some filesystems (NFS, certain network mounts) -- lock files may not actually prevent concurrent access.

**How to avoid:**
- Serialize ALL `git worktree add/remove` operations per repository on each host. Use a per-repo mutex (keyed by repo path) in the host daemon. Read operations (`git log`, `git diff`) can proceed in parallel with `--no-optional-locks`.
- Disable automatic `git gc` in worktree repositories (`git config gc.auto 0`). Run garbage collection only when no agents are active on that repo.
- Use unique branch names per worktree: `agent/{sessionId}` pattern. Never allow two worktrees to check out the same branch (git itself prevents this, but the error is confusing -- detect and report it clearly).
- Implement worktree health checks: before assigning a worktree to a session, verify the `.git` directory is not locked and the worktree is in a clean state.
- Set `core.fsmonitor=false` in worktree configs to prevent filesystem monitor contention across worktrees.

**Warning signs:**
- Intermittent "fatal: Unable to create '.git/index.lock': File exists" errors in session logs
- One agent session failure cascading to failures in other sessions using the same repo
- `git status` returning incorrect results in a worktree after another worktree committed

**Phase to address:**
Git Worktree Isolation phase -- the per-repo serialization must be in the host daemon before any real worktree operations are attempted. This cannot be safely tested with a stub implementation.

---

### Pitfall 4: Host Inventory Discovery Becomes Stale, Placement Decisions Go Wrong

**What goes wrong:**
Host inventory (installed agent CLIs, versions, disk space, available tools) is discovered once and cached. A host runs out of disk space, an agent CLI is upgraded, or a tool is removed. The coordinator's cached inventory still shows the old state. It places a session on a host that no longer has the required agent CLI version, or on a host with 500MB of disk remaining. The session fails with a cryptic remote error that is difficult to diagnose because the coordinator thought the host was healthy.

**Why it happens:**
The current `SshBackend.GetInventoryAsync` reads from the host registry (DB-backed). Host metrics are polled by `HostMetricPollingService`, but only for CPU/memory -- not for agent CLI presence, versions, disk space, or tool availability. The `NodeCapability` record has `CpuTotal` and `MemTotalMb` fields that are hardcoded to 0 in the SSH backend. Inventory discovery is expensive (SSH to each host, run multiple commands, parse output) and the temptation is to do it infrequently.

**How to avoid:**
- Separate "slow discovery" (agent CLIs installed, versions, tools -- changes rarely, poll every 5-10 minutes) from "fast health" (CPU, memory, disk, active session count -- changes constantly, poll every 30 seconds as `HostMetricPollingService` already does).
- Add disk space to the fast health poll. Disk exhaustion from worktrees is the most common placement failure mode.
- Mark hosts as "degraded" when health checks fail, not "offline." A host with high CPU but valid agent CLIs can still accept low-priority work. The `HostRecord.Enabled` boolean is too coarse.
- Implement a `HostCapability` enum (or tag set) that `SimplePlacementEngine` uses: `HasClaudeCLI`, `HasCodexCLI`, `HasGitWorktreeSupport`, `DiskSpaceAboveThreshold`. Placement fails fast with a clear error ("No host with Claude CLI v4.1+ and 2GB free disk") instead of a generic "No eligible node."
- The agent version discovery should run on the host daemon side, not by the coordinator SSHing in and running `claude --version`. The daemon knows what is installed locally.

**Warning signs:**
- Sessions fail on start with errors like "claude: command not found" despite the host being registered
- Placement decisions consistently pick the same overloaded host while others are idle
- `NodeCapability.CpuTotal` and `MemTotalMb` remain at 0 in production

**Phase to address:**
Host Inventory phase -- but the placement engine upgrade (resource-aware scheduling) must happen in the Multi-Agent Coordination phase since they are tightly coupled. Shipping inventory without upgrading placement is useless.

---

### Pitfall 5: Coordinator-Daemon State Divergence Under Network Partitions

**What goes wrong:**
The coordinator sends "pause session X" to the host daemon via SSH. The SSH connection drops before the daemon acknowledges. The coordinator marks the session as Paused in its DB. The daemon never received the command, so the agent keeps running. The user sees "Paused" in the dashboard while the agent is actively modifying code. A subsequent "resume" command is nonsensical -- the agent was never paused. Now the coordinator and daemon have permanently divergent state that can only be resolved by stopping and restarting the session.

**Why it happens:**
The current `SshBackend.SendInputAsync` is fire-and-forget with the SSH connection -- it calls `connection.ExecuteCommandAsync` and assumes success if no exception is thrown. SSH command execution is "at most once" -- if the connection drops mid-command, you do not know whether the remote side received and processed it. This is acceptable for fire-and-forget sessions (the v1.0 use case) but catastrophic for interactive steering where state must be synchronized.

**How to avoid:**
- Implement command acknowledgment: every control command (pause, resume, redirect, stop) gets a unique command ID. The daemon responds with an ack containing the command ID. The coordinator does not update its state until the ack is received.
- Implement state reconciliation: periodically (every 10-30 seconds), the coordinator queries the daemon for the authoritative state of each active session. If states diverge, the daemon's state wins (it is closer to the truth).
- Timeout unacknowledged commands: if a pause command is not acked within 5 seconds, mark the session state as "PauseRequested" (not "Paused") and show this to the user. Retry the command or alert the operator.
- The `ISshHostConnection` interface needs a method for request-response commands (send + wait for ack) distinct from the fire-and-forget `ExecuteCommandAsync`.

**Warning signs:**
- Session state in UI disagrees with what you see when SSHing to the host manually
- "Resume" commands have no effect because the session was never actually paused
- Heartbeat events from the daemon show the session as Running while the DB says Paused

**Phase to address:**
Interactive Steering phase -- the ack-based command protocol must be designed before any steering commands are implemented. Without it, every control operation is unreliable.

---

### Pitfall 6: Merge Conflict Avalanche from Parallel Worktree Agents

**What goes wrong:**
Three agents work on the same repository in separate worktrees, each on their own branch. Agent A modifies `Program.cs` and merges. Agent B also modified `Program.cs` -- its merge now has conflicts. Agent C modified a file that imports from a file Agent A renamed. Two of three agent branches are now unmergeable without human intervention. The platform successfully ran all three agents in parallel but created more integration work than it saved.

**Why it happens:**
Git worktrees provide filesystem isolation but not semantic isolation. Multiple agents touching the same codebase will inevitably modify the same files -- package manifests, configuration files, shared module registrations, and central routing files are magnets for concurrent modification. The Upsun developer blog notes: "Two agents adding separate features will often need to register those features in the same central file." The platform has no mechanism to detect or prevent overlapping file access across worktrees.

**How to avoid:**
- At dispatch time, define file ownership boundaries per agent task when possible. "Agent A works on `/src/Feature1/**`", "Agent B works on `/src/Feature2/**`". This is imperfect but reduces overlap.
- Implement a "conflict pre-check" before merging: after all sub-agents complete, diff their branches pairwise to detect conflicts before attempting any merge. Report conflicts to the operator rather than silently failing.
- For shared files (package.json, .csproj, routing config), consider a "merge coordinator" pattern: one agent is designated as the integrator that sequentially applies changes from other agents.
- Accept that parallel agents on the same repo will produce merge conflicts. Design the workflow around this reality: agents produce PRs, human reviews and resolves conflicts, NOT agents auto-merge.
- The project's "Out of Scope" section already says "Auto-merge of agent PRs -- human-in-the-loop for merges." Enforce this boundary in the system design -- the platform should never attempt automatic conflict resolution.

**Warning signs:**
- PR reviews show merge conflicts in boilerplate files (csproj, package.json, imports)
- Operators report spending more time resolving conflicts than they saved with parallelism
- Agents are assigned overlapping scopes because the dispatcher does not track file ownership

**Phase to address:**
Git Worktree Isolation phase -- the conflict pre-check should be a built-in operation. The file ownership boundaries can be deferred to a later iteration but should be designed for in the data model (add `ScopeHint` to `StartSessionRequest`).

---

### Pitfall 7: Resource-Aware Placement Without Real-Time Metrics Is Worse Than Random

**What goes wrong:**
The placement engine is upgraded to consider host resource utilization. But the metrics it uses are 30-60 seconds stale (from `HostMetricPollingService`). It places 3 new sessions on a host that was 40% CPU when last polled but is now 95% CPU because a previous agent started a build. All 3 sessions degrade, the host becomes unresponsive, and the session monitor marks them as Failed due to heartbeat timeout. Random placement would have statistically distributed the load better than "smart" placement with stale data.

**Why it happens:**
The current `HostMetricPollingService` polls every 30 seconds. In a burst of session starts (common when a multi-agent task dispatches sub-agents), multiple placement decisions happen within a single polling interval. They all see the same snapshot and all pick the same "best" host -- a classic thundering herd problem. The `SimplePlacementEngine.ChooseNode` uses `FirstOrDefault()`, which is deterministic -- given the same input, it always picks the same host.

**How to avoid:**
- Add a "pending sessions" counter per host that increments immediately at placement time (before the session actually starts) and decrements when the session is confirmed running or fails to start. This provides instantaneous load awareness without waiting for metrics.
- Add jitter/randomization to placement among equally-eligible hosts. When 3 hosts all have similar metrics, pick randomly rather than deterministically.
- Implement admission control: if a host's pending + active session count exceeds a threshold, reject new placements there even if metrics look good. A configurable `MaxConcurrentSessions` per host.
- For burst dispatches (multi-agent sub-agent spawning), introduce a brief delay (100-500ms) between successive placements to allow the pending counter to propagate. This trades latency for better distribution.
- The `NodeCapability` record needs an `ActiveSessionCount` field populated from the coordinator's session database, not just hardware specs.

**Warning signs:**
- Multiple sessions start on the same host within seconds, overwhelming it
- Hosts alternate between idle and overloaded while placement engine reports even distribution
- `HostMetricPollingService` shows host CPU spike immediately after placement

**Phase to address:**
Multi-Agent Coordination phase -- the pending-session counter and admission control must ship alongside the sub-agent spawning mechanism. Without them, the first multi-agent dispatch will demonstrate the thundering herd.

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| Overloading `SendInputRequest` for control commands (pause/resume) | No new API surface | Control and data paths entangled, impossible to apply different policies or rate limits | Never -- separate from the start |
| Git worktree stub (current `GitWorktreeProvider` writes metadata file, no real git operations) | Unblocks session model | Must be completely replaced; no incremental path from stub to real implementation | Only during design phase. Replace with real git operations before testing worktree features |
| Flat session hierarchy (no parent-child relationship) | Simpler DB schema | Cannot enforce sub-agent depth limits, cannot trace cascading failures, cannot kill a task and all its sub-agents | Only for Phase 1 single-agent sessions. Add `ParentSessionId` before multi-agent coordination |
| Polling-only host health (no push from daemon) | No daemon protocol changes | 30-second staleness for placement decisions; missed transient failures | Acceptable for v1.1 if combined with pending-session counter. Push-based health can come later |
| Boolean `Enabled` flag on hosts | Simple on/off | Cannot express "degraded" (high CPU but functional), "draining" (no new sessions, existing ones continue), or "maintenance" | Replace with `HostStatus` enum (Online, Degraded, Draining, Offline) in Host Inventory phase |

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| Git worktree + SSH | Running `git worktree add` from the coordinator via SSH as a one-off command | The host daemon should manage worktrees locally. Coordinator sends "ensure worktree for repo X, ref Y" and daemon handles the git operations with local locking |
| Interactive steering + SSE | Sending pause/resume status only as SSE events without updating session DB state | Always update DB state first, THEN emit SSE event. Otherwise a client reconnection after a state change sees the old state from the DB while the SSE event is lost |
| Sub-agent spawning + event service | Sub-agent events routed only to sub-agent's SSE stream, parent task's stream shows nothing | Fan out sub-agent events to the parent session's stream as well (with a prefix/tag). The operator watches the parent task and needs to see sub-agent activity |
| Host inventory + placement engine | Discovering inventory in `GetInventoryAsync` (called at session start time) adds latency | Pre-cache inventory with background polling. `GetInventoryAsync` returns cached data. Discovery runs on its own schedule |
| Git worktree cleanup + session stop | Cleaning up the worktree directory immediately on session stop | The operator may want to inspect the worktree after the session ends. Implement a configurable cleanup delay (e.g., 1 hour) or "keep worktree" flag. Clean up on a sweeper schedule |
| Multi-agent dispatch + approval flow | Sub-agent tasks inheriting the parent's approval settings | Each sub-agent should have its own approval policy. A parent with `AcceptRisk=true` should not automatically grant risk acceptance to all children |

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| Per-repo git lock serialization under high parallelism | Worktree creation queues up, sessions wait 10+ seconds to start | Pool pre-created worktrees per popular repo. Create worktrees in advance during idle time | 5+ concurrent sessions on the same repo |
| Host inventory discovery over SSH for every placement decision | Session start latency spikes to 3-5 seconds | Cache inventory, refresh on schedule, serve placement from cache | 3+ hosts with 30+ concurrent session starts |
| Unbounded SSE event replay on reconnection | Client reconnects with `Last-Event-ID: 1` after being away for hours; coordinator fetches thousands of events from DB | Cap replay to last N events or last T minutes. Return a "gap" indicator if events were truncated | Long-running sessions (>1 hour) with verbose agent output |
| State reconciliation polling all sessions on all hosts | Coordinator saturates SSH connections with status queries | Only reconcile sessions that have not sent a heartbeat recently. Healthy sessions need no reconciliation | 50+ active sessions across 10+ hosts |
| Event fan-out for sub-agent hierarchy | Parent task's SSE stream receives events from all 10 sub-agents, overwhelming the client | Aggregate sub-agent events: send summary events to parent stream, full events only on the sub-agent's own stream | Multi-agent tasks with 5+ sub-agents producing verbose output |

## Security Mistakes

| Mistake | Risk | Prevention |
|---------|------|------------|
| Sub-agents inheriting parent session's SSH credentials without scope restriction | Compromised or misbehaving sub-agent has full access to the host | Sub-agents should run with the same or more restricted permissions than the parent. Never escalate trust through spawning |
| Host inventory exposing agent CLI paths and versions via API | Reconnaissance information for attackers targeting specific CLI vulnerabilities | Inventory details should be internal to the coordinator. Expose only capability flags (hasClaudeCLI: true) to external APIs, not paths/versions |
| Interactive steering commands not subject to sanitization | Redirect command contains shell injection that bypasses input sanitization because it is treated as a control command, not data | All commands that result in text being sent to the agent process must pass through sanitization, regardless of whether they are classified as "control" or "data" |
| Git worktree branches exposing session IDs in public repos | Session IDs are predictable or enumerable, enabling unauthorized session access | Use opaque branch names (e.g., `agent/abc123` not `agent/ssh_<guid>`) and clean up branches after merging. Session IDs should not be guessable |

## UX Pitfalls

| Pitfall | User Impact | Better Approach |
|---------|-------------|-----------------|
| Pause/resume with no visual feedback for 5+ seconds | User clicks "Pause", nothing visible changes, clicks again (sending resume), agent was never actually paused | Show immediate optimistic UI state ("Pausing...") and update to confirmed state ("Paused") when daemon ack arrives. Show error state if ack times out |
| Sub-agent tree not visible in dashboard | Operator sees 10 sessions but cannot tell which are related to the same task | Display session hierarchy: parent task with expandable sub-agents. Show aggregate status (3/5 sub-agents complete) |
| Host inventory shown as a flat table with raw numbers | Operator cannot quickly identify which host to use or which host is unhealthy | Show hosts as cards with health indicators (green/yellow/red), installed agents as badges, and active session count prominently displayed |
| Git worktree merge conflicts reported as session failures | Operator thinks the agent crashed when it actually completed successfully but the merge failed | Separate "agent task status" from "integration status." Agent succeeded. Merge has conflicts. These are different things |
| Multi-agent dispatch with no cost estimate | Operator dispatches a 10-agent task not realizing it will cost 15x a single agent session | Show estimated cost/token budget before confirming multi-agent dispatch. Require explicit confirmation for tasks above a threshold |

## "Looks Done But Isn't" Checklist

- [ ] **Interactive Steering:** Often missing the "PauseRequested" intermediate state -- verify that the UI shows the intermediate state and the daemon confirms the transition
- [ ] **Sub-Agent Spawning:** Often missing depth limits -- verify that a sub-agent cannot spawn its own sub-agents beyond the configured depth (test with depth=1, depth=2, depth=0)
- [ ] **Host Inventory:** Often missing disk space monitoring -- verify that disk space is checked before worktree creation and that placement engine rejects hosts below threshold
- [ ] **Git Worktree Cleanup:** Often missing orphan detection on host restart -- verify that the host daemon cleans up worktrees for sessions that no longer exist in the coordinator DB
- [ ] **State Reconciliation:** Often missing the reconciliation loop -- verify that after a coordinator restart, session states are updated from host daemon queries within 60 seconds
- [ ] **Command Acknowledgment:** Often missing timeout handling -- verify that unacknowledged commands are retried or reported to the operator, not silently dropped
- [ ] **Event Fan-Out:** Often missing sub-agent event routing to parent stream -- verify that watching a parent task's SSE stream shows activity from its sub-agents
- [ ] **Placement Pending Counter:** Often missing the decrement on failure -- verify that a failed session start decrements the pending counter so the host is not permanently penalized
- [ ] **Worktree Branch Naming:** Often missing uniqueness enforcement -- verify that two sessions cannot create worktrees on the same branch of the same repo

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| Coordinator-daemon state divergence | MEDIUM | Run state reconciliation immediately. Daemon state wins. Update DB. Emit corrective SSE events to connected clients |
| Runaway sub-agent cascade | HIGH | Kill the parent session (which should cascade-kill all children). If cascade kill is not implemented, enumerate all sessions with the parent's ID and kill individually. Audit token spend |
| Git worktree corruption (shared .git) | HIGH | Stop ALL sessions using that repo's worktrees. Run `git worktree prune` and `git fsck`. Recreate worktrees from clean state. Sessions in progress lose their work |
| Thundering herd on placement | LOW | Restart failed sessions with a stagger. Implement pending-session counter to prevent recurrence. No data loss since sessions failed at start |
| Merge conflict avalanche | MEDIUM | Triage which agent branches conflict. Merge the highest-value branch first. Rebase remaining branches or re-run those agents against the updated main. Operator intervention required |
| Stale host inventory causing placement failures | LOW | Force an immediate inventory refresh (`POST /hosts/{id}/discover`). Retry the failed session start. Add disk space to fast health checks to prevent recurrence |
| Interactive command lost (no ack) | LOW | Resend the command. If the session is in an unknown state, query the daemon for authoritative state and update accordingly |

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| State machine explosion (interactive steering) | Interactive Steering | Full state transition diagram documented; every (state, command) pair tested; no impossible states reachable |
| Unbounded sub-agent spawning | Multi-Agent Coordination | Depth limit enforced; test spawns sub-agents at max depth and verifies rejection; fleet-wide session cap tested |
| Git shared `.git` corruption | Git Worktree Isolation | Per-repo mutex tested with concurrent worktree creation; `gc.auto=0` set in worktree repos; lock contention test passes |
| Stale host inventory | Host Inventory | Inventory refreshes on schedule; placement engine uses cached + pending-session data; disk space included in fast health |
| Coordinator-daemon state divergence | Interactive Steering | Command ack protocol implemented; reconciliation loop runs every 30s; test simulates dropped SSH connection mid-command |
| Merge conflict avalanche | Git Worktree Isolation | Conflict pre-check runs before merge attempt; operator sees conflict report; no auto-merge attempted |
| Thundering herd placement | Multi-Agent Coordination | Pending-session counter implemented; burst dispatch test shows even distribution; admission control rejects over-capacity hosts |

## Sources

- [How we built our multi-agent research system](https://www.anthropic.com/engineering/multi-agent-research-system) -- Anthropic Engineering Blog (sub-agent spawning limits, task delegation failures, observability challenges)
- [Git worktrees for parallel AI coding agents](https://devcenter.upsun.com/posts/git-worktrees-for-parallel-ai-coding-agents/) -- Upsun Developer Center (port conflicts, disk space, database isolation, merge conflicts)
- [Using Git Worktrees for Multi-Feature Development with AI Agents](https://www.nrmitchi.com/2025/10/using-git-worktrees-for-multi-feature-development-with-ai-agents/) -- Nick Mitchinson (practical worktree limitations)
- [Git Worktrees for AI Coding: Run Multiple Agents in Parallel](https://dev.to/mashrulhaque/git-worktrees-for-ai-coding-run-multiple-agents-in-parallel-3pgb) -- DEV Community (disk space benchmarks, IDE gaps)
- [AI Agent Orchestration Patterns](https://learn.microsoft.com/en-us/azure/architecture/ai-ml/guide/ai-agent-design-patterns) -- Microsoft Azure Architecture Center (orchestrator-worker patterns, coordination failures)
- [Multi-Agent Coordination Gone Wrong? Fix With 10 Strategies](https://galileo.ai/blog/multi-agent-coordination-strategies) -- Galileo AI (task ownership, cascading failures)
- [How Git Worktrees Changed My AI Agent Workflow](https://nx.dev/blog/git-worktrees-ai-agents) -- Nx Blog (monorepo worktree scaling)
- [Designing the Multi-Agent Development Environment](https://alexlavaee.me/blog/parallel-agent-sessions-infrastructure-gap/) -- Alex Lavaee (infrastructure gaps for parallel agents)
- Existing codebase analysis: `SshBackend.cs`, `SessionCoordinator.cs`, `SimplePlacementEngine.cs`, `GitWorktreeProvider.cs`, `HostMetricPollingService.cs`, `DurableEventService.cs`

---
*Pitfalls research for: AgentSafeEnv v1.1 -- interactive steering, multi-agent coordination, host inventory, git worktree isolation*
*Researched: 2026-03-09*
