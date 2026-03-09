# Feature Research

**Domain:** Multi-agent orchestration platform (v1.1 milestone features)
**Researched:** 2026-03-09
**Confidence:** MEDIUM-HIGH

## Context

v1.0 already ships: session CRUD, SSH execution, SSE streaming, CLI with live watch/approvals, Blazor dashboard with fleet overview, history API, bidirectional input (`SendInputAsync`), host metric polling, incremental SSE patching, trust tiers, sanitization. 11,989 LOC across 178 files.

This research covers ONLY the four new v1.1 feature areas:
1. Interactive sessions (pause/resume/redirect)
2. Multi-agent coordination (dispatch, sub-agent spawning, resource-aware scheduling)
3. Host inventory (tool discovery, version detection)
4. Git worktree isolation per session

---

## Feature Landscape

### Table Stakes (Users Expect These)

Features that users assume exist once interactive/coordination capabilities are advertised. Missing any of these and the v1.1 story feels incomplete.

| Feature | Why Expected | Complexity | Depends On (Existing) |
|---------|--------------|------------|----------------------|
| **Send follow-up instructions to running session** | Already works via `SendInputAsync` + bidirectional input (v1.0). Users expect to type a new prompt and have the agent change direction mid-task. Claude Code supports this natively via stdin. Polish existing, not new build. | LOW | Bidirectional input (v1.0) |
| **Session pause (suspend agent process)** | Standard process control. Users expect to freeze an agent temporarily to review output before it continues. Translates to SIGTSTP over SSH. Every terminal user understands Ctrl+Z. | MEDIUM | SSH backend, session state machine |
| **Session resume after pause** | Complement of pause. Send SIGCONT to resume a suspended process. Must extend `SessionState` enum to include `Paused` state (currently: Pending, Running, Stopped, Failed). | LOW | Pause feature |
| **Git worktree creation per session** | Claude Code has native `--worktree` flag. Industry consensus (Claude Squad, Conductor, every blog post) is that parallel agents on the same repo WILL hit merge conflicts without isolation. Non-negotiable for multi-agent on shared repos. | MEDIUM | `GitWorktreeProvider` (stub exists in codebase), SSH backend |
| **Git worktree cleanup on session end** | Worktrees leak disk if not cleaned up. Must `git worktree remove` and prune references when session completes or is force-killed. The existing `StopAsync` lifecycle hooks are the right place. | LOW | Worktree creation, session lifecycle |
| **Host tool inventory (which agent CLIs are installed)** | Before dispatching to a host, the system must know if `claude`, `codex`, etc. are available. Without this, sessions fail at start with cryptic errors. `GetInventoryAsync` exists but only returns basic node capability (CPU/mem), not installed tools. | MEDIUM | Host registry (v1.0), SSH backend |
| **Agent CLI version detection** | Different CLI versions have different capabilities (`--worktree` only in Claude Code 1.0.20+). Version detection prevents dispatching incompatible flags to older agent versions. | LOW | Tool inventory |
| **Resource-aware placement (don't overload hosts)** | `SimplePlacementEngine` currently picks `FirstOrDefault()` from eligible nodes. Users expect the system not to stack 5 agents on a host with 90% CPU while another sits idle. Host metrics polling already exists (v1.0). | MEDIUM | Host metrics polling (v1.0), placement engine |
| **Basic task dispatch to named host** | Already partially exists via `TargetHostId` in `SessionRequirements`. Polish the existing path so it works reliably with validation. | LOW | Placement engine (v1.0) |

### Differentiators (Competitive Advantage)

Features that set AgentSafeEnv apart from manual worktree/tmux workflows and single-machine tools.

| Feature | Value Proposition | Complexity | Depends On |
|---------|-------------------|------------|------------|
| **Prompt redirect (change task direction mid-session)** | Beyond just sending follow-up input -- detect when an agent is idle/waiting via output parsing, then inject a new high-level directive. This is the "steering wheel" that makes interactive sessions genuinely interactive, not just input forwarding. | HIGH | Session state detection, agent output pattern matching |
| **Sub-agent spawning from parent session** | A running session can request the orchestrator to spin up helper agents on other hosts. Parent-child relationship tracked in DB, child results aggregated back to parent's event stream. This is the core multi-agent differentiator -- no self-hosted tool does this. | HIGH | Session coordinator, placement engine, event system |
| **Automatic worktree branch naming** | Name worktree branches based on session ID + prompt summary (e.g., `agent/ssh_abc123/fix-auth-bug`). Makes `git branch -a` readable when running dozens of parallel agents. | LOW | Worktree creation |
| **Resource-aware auto-scheduling with scoring** | Orchestrator picks the least-loaded eligible host automatically. Scoring combines CPU usage, memory usage, active session count with configurable weights. Operators set constraints, system optimizes placement. | MEDIUM | Host metrics, placement engine |
| **Host capability fingerprinting** | SSH probe discovers: installed agent CLIs + versions, git version, disk space, Node/Python/Docker availability. Cached with TTL. Enables smart dispatch and prevents "session started but agent not found" failures. | MEDIUM | SSH backend, host registry |
| **Coordinated multi-session launch** | Single API call to launch N sessions across the fleet with operator-provided task breakdown. One `POST /sessions/batch` instead of N individual calls. System places and monitors all sessions, reports aggregate status. | MEDIUM | Session coordinator, placement engine |
| **Session dependency graph (parent-child tracking)** | Parent sessions can wait for child sessions to complete before continuing. DB tracks parent/child relationships. Dashboard shows session tree visualization. | HIGH | Sub-agent spawning, session state machine |
| **Worktree merge-readiness check** | After session completes in a worktree, run `git diff --stat` against base branch and surface results in dashboard/CLI. Operator sees "Agent changed 3 files, +47/-12 lines" at a glance. | LOW | Worktree creation, SSH backend |
| **Health-check probes for hosts** | Periodic SSH-based probes beyond CPU/memory: disk space, agent CLI responsiveness, git connectivity. Auto-mark hosts as degraded/unavailable. | MEDIUM | Host registry, background service |

### Anti-Features (Commonly Requested, Often Problematic)

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| **Agent-to-agent direct communication** | "Let agents collaborate directly" | Debugging nightmare. Race conditions. Agents hallucinate protocols. PROJECT.md explicitly excludes this. | Orchestrator-mediated coordination: parent session sends structured requests to coordinator, which spawns/manages child sessions. Results flow back through SSE events. |
| **Automatic task decomposition** | "Let AI break down the task into sub-tasks" | Unreliable decomposition leads to wasted compute. Wrong subtask boundaries cause integration failures. PROJECT.md excludes this. | Operator decomposes manually. System executes the plan. Provide templates/wizards for common decomposition patterns. |
| **Auto-merge of agent worktree results** | "Automatically merge when agent is done" | Merge conflicts, broken builds, untested code landing in main. PROJECT.md excludes this. | Surface diff stats and merge-readiness in dashboard. Operator reviews and merges manually via `git merge`. |
| **Real-time token-by-token thought streaming** | "Show me what the agent is thinking" | Massive SSE bandwidth. Most agent CLIs don't expose chain-of-thought cleanly. Would require parsing and forwarding every token. | Stream stdout/stderr as today. Add structured event markers for key transitions (tool use started, file edit, approval request) as new `SessionEventKind` values. |
| **Automatic tool installation on hosts** | "Install missing CLIs automatically" | Security risk (arbitrary code execution). Package manager differences across distros. Fragile. PROJECT.md excludes this. | Report missing tools in inventory. Operator installs manually. Provide detection, not remediation. |
| **Global session state synchronization** | "Keep all parallel sessions aware of each other's changes" | Agents work on different tasks in different worktrees. Forcing sync creates conflicts, stalls, and wasted context window. | Independent worktrees. Merge results after completion. Track dependencies via session parent-child graph, not shared file state. |
| **Idle detection and auto-redirect** | "Automatically detect when agent is stuck and redirect" | Agent output patterns are agent-specific and fragile. False positives would interrupt productive work. | Surface session duration and recent activity rate in dashboard. Let operator decide when to intervene. Manual steering, not automatic. |

---

## Feature Dependencies

```
[Host Tool Inventory]
    |
    +--requires--> [SSH Backend] (exists v1.0)
    +--requires--> [Host Registry] (exists v1.0)
    |
    +--enables--> [Agent CLI Version Detection]
    |                 |
    |                 +--enables--> [Smart Worktree Flag Support]
    |                                  (verify agent CLI supports --worktree before passing it)
    |
    +--enables--> [Resource-Aware Placement]
                      |
                      +--requires--> [Host Metrics Polling] (exists v1.0)
                      +--enables--> [Auto-Scheduling]
                      +--enables--> [Sub-Agent Spawning]

[Session Pause/Resume]
    +--requires--> [SessionState Enum Extension] (add Paused = 4)
    +--requires--> [HostCommandProtocol Extension] (pause-session, resume-session)
    +--requires--> [SSH Signal Sending] (SIGTSTP/SIGCONT via kill command)
    +--independent-of--> [other v1.1 features]

[Git Worktree Lifecycle]
    +--requires--> [Host Tool Inventory] (verify git installed + version)
    +--requires--> [SSH Backend] (execute git worktree commands remotely)
    +--enables--> [Worktree Cleanup on Session End]
    +--enables--> [Worktree Merge-Readiness Check]
    +--enables--> [Automatic Branch Naming]

[Follow-up Instructions UX]
    +--requires--> [Bidirectional Input] (exists v1.0)
    +--enhances--> [Prompt Redirect] (future)
    +--independent-of--> [other v1.1 features]

[Sub-Agent Spawning]
    +--requires--> [Session Coordinator] (exists v1.0)
    +--requires--> [Resource-Aware Placement] (don't overload hosts)
    +--requires--> [Host Tool Inventory] (verify target host has needed tools)
    +--enables--> [Session Dependency Graph]
    +--enables--> [Coordinated Multi-Session Launch]
```

### Dependency Notes

- **Host Tool Inventory is the foundation**: Almost every new feature depends on knowing what is installed where. Build this first. It unblocks worktree creation (need git), smart placement (need tool availability), and sub-agent spawning (need target host validation).
- **Session Pause/Resume is independent**: No dependency on other v1.1 features. Can be built in parallel with inventory work. Only touches state machine and SSH signal sending.
- **Resource-Aware Placement unlocks multi-agent safely**: Without smart placement, spawning multiple agents risks degrading the fleet. Must precede sub-agent spawning.
- **Git Worktree depends on inventory**: Need to verify git is installed and version >= 2.5 (worktree support added in git 2.5) before attempting creation.
- **Sub-Agent Spawning is the capstone**: Depends on inventory + placement + events. Build last.
- **Follow-up Instructions UX conflicts with nothing**: Independent UX polish that can happen anytime.

---

## Implementation Phases (v1.1 Recommendation)

### Phase 1: Foundation (Build First)

These features have zero dependencies on other v1.1 work and unblock everything else.

- [ ] **Host tool inventory via SSH probing** -- Run `which claude && claude --version` (and equivalents for codex, git, node, python, docker) over SSH. Cache results in DB with TTL (default: 1 hour). Extend `HostRecord` model with `Dictionary<string, ToolInfo> InstalledTools` where `ToolInfo` has `Version`, `Path`, `LastChecked`.
- [ ] **SessionState enum extension** -- Add `Paused = 4` to avoid breaking existing persisted data (existing values: 0-3). Update all state transition logic, EF migration, SSE event handling, dashboard display, CLI status output.
- [ ] **Session pause/resume via signals** -- Extend `HostCommandProtocol` with `pause-session` and `resume-session` commands. SSH backend sends `kill -TSTP <pid>` / `kill -CONT <pid>`. Add `PauseAsync` and `ResumeAsync` to `ISessionBackend`. New API endpoints: `POST /sessions/{id}/pause`, `POST /sessions/{id}/resume`.

### Phase 2: Isolation (Build Second)

Depends on Phase 1 inventory for git version verification.

- [ ] **Git worktree lifecycle (create/cleanup)** -- Replace `GitWorktreeProvider` stub with real SSH-based implementation. On session start: `git worktree add .claude/worktrees/{sessionId} -b agent/{sessionId}` on the remote host. On session end: `git worktree remove .claude/worktrees/{sessionId}`. Branch naming convention: `agent/{sessionId}/{sanitized-prompt-prefix}`.
- [ ] **Resource-aware placement engine** -- Replace `SimplePlacementEngine.FirstOrDefault()` with scoring function. Score = weighted combination of (1 - cpuPercent), (1 - memUsed/memTotal), (1 - activeSessionCount/maxSessions). Pick highest-scoring eligible node. Default weights: cpu=0.3, mem=0.3, sessions=0.4.
- [ ] **Follow-up instruction UX polish** -- Enhance CLI and Blazor to distinguish "initial prompt" from "follow-up steering" visually. Show conversation threading in session detail view. Add "Send follow-up" button/command that is distinct from approval responses.

### Phase 3: Coordination (Build Last)

Depends on Phase 1+2 for safe placement and tool verification.

- [ ] **Sub-agent spawning API** -- New endpoint: `POST /sessions/{id}/spawn`. Creates child session with `ParentSessionId` field. Events from children include `parentSessionId` in metadata and bubble to parent's SSE stream. New `SessionEventKind.ChildSpawned` and `SessionEventKind.ChildCompleted`.
- [ ] **Coordinated multi-session launch** -- New endpoint: `POST /sessions/batch` accepting array of `StartSessionRequest`. Placement engine distributes across fleet. Returns array of session IDs. Aggregate status endpoint: `GET /sessions/batch/{batchId}`.
- [ ] **Session parent-child tracking** -- Add `ParentSessionId` nullable FK to `SessionEntity`. Dashboard shows session tree. CLI `watch` can follow a parent and all children.

### Defer to v1.2+

- [ ] **Session dependency DAG execution** -- Full DAG with fan-out/fan-in and conditional branching. Too complex for v1.1; requires proven sub-agent spawning first.
- [ ] **Prompt redirect with idle detection** -- Requires parsing agent-specific output patterns to detect "waiting" vs "working" state. Fragile across agent types. Start with manual follow-up instructions in v1.1.
- [ ] **Health-check probes** -- Nice but not blocking. Host metrics polling (v1.0) provides basic health signal. Add dedicated probes when fleet grows beyond 10 hosts.
- [ ] **Worktree merge-readiness check** -- Low complexity but not core to v1.1 goals. Easy to add after worktree lifecycle is proven.

---

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| Host tool inventory | HIGH | MEDIUM | P1 |
| Session pause/resume | HIGH | MEDIUM | P1 |
| Git worktree lifecycle (create + cleanup) | HIGH | MEDIUM | P1 |
| Resource-aware placement | HIGH | MEDIUM | P1 |
| Agent CLI version detection | MEDIUM | LOW | P1 |
| Follow-up instruction UX | MEDIUM | LOW | P1 |
| Sub-agent spawning | HIGH | HIGH | P2 |
| Coordinated multi-session launch | MEDIUM | MEDIUM | P2 |
| Session parent-child tracking | MEDIUM | MEDIUM | P2 |
| Auto worktree branch naming | MEDIUM | LOW | P2 |
| Worktree merge-readiness check | MEDIUM | LOW | P2 |
| Session dependency DAG | MEDIUM | HIGH | P3 |
| Prompt redirect with idle detection | MEDIUM | HIGH | P3 |
| Health-check probes | LOW | MEDIUM | P3 |

**Priority key:**
- P1: Must have -- core v1.1 value proposition, unblocks other features
- P2: Should have -- completes the multi-agent coordination story
- P3: Nice to have -- defer to v1.2+

---

## Key Design Decisions

### Git Worktree Strategy: Orchestrator-Managed (Not Agent-Managed)

Two approaches exist:
1. **Let agent CLI handle worktrees** -- Pass `--worktree` to Claude Code. Simple but only works for agents that support the flag.
2. **Orchestrator creates worktrees before agent launch** -- `git worktree add` via SSH, then launch agent in that directory. Works for ALL agent types.

**Recommendation: Option 2 (orchestrator-managed).** This is agent-agnostic, matches the platform's "agents run unmodified" constraint from PROJECT.md, and gives the orchestrator full control over lifecycle and cleanup. For Claude Code specifically, do NOT also pass `--worktree` (double worktree nesting would be problematic). The existing `WorktreeDescriptor` and `IWorktreeProvider` abstractions support this approach -- the `GitWorktreeProvider` stub just needs a real SSH-based implementation.

### Worktree Location Convention

Create worktrees under the repo's `.worktrees/` directory on the remote host:
```
/repo/.worktrees/{sessionId}/     -- worktree directory
branch: agent/{sessionId}          -- branch name
```
This matches the community convention (`.trees/` or `.worktrees/` directory, single `.gitignore` entry).

### Session State for Pause

Extend `SessionState` enum with value 4 to avoid breaking existing persisted data:
```csharp
public enum SessionState
{
    Pending = 0,
    Running = 1,
    Stopped = 2,
    Failed = 3,
    Paused = 4
}
```

The SSH backend sends signals via the HostCommandProtocol. New commands: `pause-session` (sends SIGTSTP) and `resume-session` (sends SIGCONT). The host daemon must track the agent process PID to send signals.

### Placement Engine Scoring

Replace `FirstOrDefault()` with weighted scoring:
```
score = (1 - cpuPercent/100) * 0.3
      + (1 - memUsed/memTotal) * 0.3
      + (1 - activeSessionCount/maxSessions) * 0.4
```

Session count gets highest weight (0.4) because agent workloads are bursty -- CPU/memory spikes are transient, but context window consumption is sustained. Pick the node with the highest score among eligible candidates. Fallback: if all scores are below a threshold (e.g., 0.1), reject the request rather than overloading the fleet.

### Sub-Agent Spawning Model

Parent session requests child via structured event, not direct agent-to-agent communication:
1. Parent's output stream includes a spawn request (detected by pattern matching or explicit API call from operator)
2. Coordinator validates resources via placement engine
3. Child session created with `ParentSessionId` set
4. Child events tagged with `parentSessionId` in `Meta` dictionary
5. When child completes, `SessionEventKind.ChildCompleted` event emitted on parent's stream
6. Parent can optionally block (poll/wait for child) or continue independently

This keeps agents unmodified -- the orchestrator interprets and mediates, agents just run.

---

## Competitor/Ecosystem Feature Analysis (v1.1-Specific)

| Feature | Claude Code Native | VS Code Copilot | Claude Squad | Our Approach |
|---------|-------------------|-----------------|--------------|--------------|
| Session pause/resume | Ctrl+Z in terminal (manual) | N/A | Tmux detach/attach | SSH signal forwarding (SIGTSTP/SIGCONT) with state tracking |
| Session resume | `--resume` / `--continue` flags | Chat persistence | Tmux session persistence | Orchestrator-level resume via session state + SSH reconnection |
| Worktree isolation | `--worktree` flag (built-in) | N/A | Git worktree per session | Orchestrator-managed `git worktree add` before agent launch (agent-agnostic) |
| Parallel agents | Manual (multiple terminals/tmux) | Multiple chat windows | Tmux panes (single machine) | Fleet dispatch across machines with resource-aware placement |
| Sub-agent spawning | Subagents within single session (local) | Agent mode delegation | N/A | Cross-machine spawning via coordinator API with parent-child tracking |
| Tool inventory | N/A | Extension marketplace | Hardcoded agent list | SSH-based probing with cached results and version detection |
| Resource-aware scheduling | N/A | N/A | N/A (single machine) | Weighted scoring of CPU/mem/session count across fleet |

**Key insight:** Claude Code's `--worktree` flag works for single-user, single-machine scenarios. AgentSafeEnv's orchestrator-managed approach extends this to multi-machine fleet scenarios where the orchestrator must control the worktree lifecycle across remote hosts. The worktree is created before the agent starts and cleaned up after it stops, regardless of which agent CLI is used.

---

## Sources

- [Claude Code CLI Reference](https://code.claude.com/docs/en/cli-reference) -- session flags, --resume, --worktree
- [Claude Code built-in git worktree support announcement](https://www.threads.com/@boris_cherny/post/DVAAnexgRUj) -- official worktree feature
- [Git Worktrees: The Secret Weapon for Parallel AI Coding Agents](https://medium.com/@mabd.dev/git-worktrees-the-secret-weapon-for-running-multiple-ai-coding-agents-in-parallel-e9046451eb96) -- patterns and pitfalls
- [Claude Code Worktrees Guide](https://claudefa.st/blog/guide/development/worktree-guide) -- .worktrees directory convention, cleanup
- [Using Git Worktrees for Multi-Feature Development with AI Agents](https://www.nrmitchi.com/2025/10/using-git-worktrees-for-multi-feature-development-with-ai-agents/) -- branch naming, isolation patterns
- [Multi-Agent Orchestration Patterns 2025-2026](https://www.onabout.ai/p/mastering-multi-agent-orchestration-architectures-patterns-roi-benchmarks-for-2025-2026) -- coordination architectures
- [VS Code Multi-Agent Orchestration](https://visualstudiomagazine.com/articles/2025/12/12/vs-code-1-107-november-2025-update-expands-multi-agent-orchestration-model-management.aspx) -- session list, background agents, handoffs
- [OpenAI Agents SDK Multi-Agent](https://openai.github.io/openai-agents-python/multi_agent/) -- handoff patterns
- [How to Build Multi-Agent Systems: Complete 2026 Guide](https://dev.to/eira-wexford/how-to-build-multi-agent-systems-complete-2026-guide-1io6) -- coordination patterns
- [Parallel Workflows: Git Worktrees and Managing Multiple AI Agents](https://medium.com/@dennis.somerville/parallel-workflows-git-worktrees-and-the-art-of-managing-multiple-ai-agents-6fa3dc5eec1d) -- port conflicts, DB isolation challenges

---
*Feature research for: AgentSafeEnv v1.1 Multi-Agent & Interactive*
*Researched: 2026-03-09*
