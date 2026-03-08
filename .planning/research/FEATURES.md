# Feature Research

**Domain:** Multi-agent coding orchestration platform (fleet management for AI coding agents across machines)
**Researched:** 2026-03-08
**Confidence:** HIGH

## Feature Landscape

### Table Stakes (Users Expect These)

Features users assume exist. Missing these = product feels incomplete.

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Session launch on remote hosts | Core value prop -- deploy agents to machines from one place. Every competitor (Conductor, Claude Squad, Warp Oz, GitHub Mission Control) does this. | MEDIUM | Must support multiple agent CLIs (claude, codex, gh copilot, gemini, opencode). Wrap existing CLIs, don't replace them. |
| Real-time session output streaming | Users need to see what agents are doing now. GitHub Mission Control shows session logs inline. Conductor shows agent dashboards. Without this, orchestration is blind delegation. | MEDIUM | SSE or WebSocket from host to coordinator. Must handle multiple concurrent streams without blocking. |
| Session lifecycle management (start/stop/status) | Basic CRUD for sessions. Every orchestrator has this. Users expect to kill runaway agents instantly. | LOW | Stop must be reliable -- an agent that can't be stopped is dangerous. Include force-kill fallback. |
| Host inventory and registration | Need to know which machines are available and what agents they have installed. Without this, launching is guesswork. | LOW | Track hostname, OS, installed agent CLIs, current load. Health checks (heartbeat). |
| Multi-agent-type support | Supporting only one agent vendor is not an orchestrator, it's a wrapper. Claude Squad supports Claude, Aider, Codex, OpenCode, Amp. | MEDIUM | Each agent CLI has different invocation, output format, and interaction model. Need adapter pattern per agent type. |
| Git worktree-based isolation | Standard pattern in this space. Conductor and Claude Squad both use git worktrees to prevent agents from conflicting. Without isolation, parallel agents corrupt each other's work. | MEDIUM | Auto-create worktree per session, clean up on completion. Handle edge cases (worktree limit, disk space). |
| Diff review before merge | Users must review what agents changed before accepting. Conductor's core UX is diff-first review. Claude Squad has a diff tab. Trust-but-verify is the norm. | LOW | Show git diff of agent's worktree vs base branch. Standard git tooling. |
| CLI as primary interface | Power users (the target audience) want CLI-first. GitHub Mission Control has CLI access. Warp Oz has CLI + API. | MEDIUM | CLI must be fast, scriptable, and composable. Not a TUI-only experience -- support both interactive and non-interactive modes. |
| Session history and logs | Need to review what happened after the fact. Audit trail of agent actions, not just live streaming. Warp Oz auto-tracks every agent session. | LOW | Store session output, commands executed, duration, outcome (success/fail/aborted). |
| Task-based (fire-and-forget) sessions | Give agent a task, walk away, review results later. OpenAI Codex, Google Jules, GitHub Copilot agent all support this model. | LOW | Agent runs to completion (or timeout), stores output and diff. Notify on completion. |

### Differentiators (Competitive Advantage)

Features that set the product apart. Not required, but valuable.

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| Multi-machine fleet management | Most competitors (Claude Squad, Conductor) run on a single machine. AgentSafeEnv orchestrates across 5-10 machines. This is a fleet management tool, not a local multiplexer. Only Warp Oz operates at fleet scale, but it's cloud-hosted. Self-hosted fleet management is a gap. | HIGH | Requires host agent daemon, network connectivity, coordinator service. Core architectural differentiator. |
| Pluggable execution backends (Nomad/SSH/containers) | No competitor offers this flexibility. Most are hardcoded to one execution model (tmux, Docker, cloud VMs). Supporting Nomad for proper scheduling + SSH for direct access + future containers gives deployment flexibility. | HIGH | Abstract execution behind interface. Start with SSH (simplest), add Nomad (better isolation/scheduling). |
| Approval/elevation flow (human-in-the-loop gating) | Agents needing permission for destructive actions is an emerging pattern (LangGraph interrupt(), OpenAI Agents SDK HITL). Most coding agent orchestrators lack this -- they're either fully autonomous (YOLO mode) or fully interactive. Configurable approval gates are the middle ground. | HIGH | Policy-driven: define which actions need approval. Notification push to operator. Timeout handling (what if nobody approves?). |
| Resource usage visibility (CPU, memory, tokens) | Cost awareness is critical when running 5-10 agents simultaneously. Devin tracks ACU consumption. Warp Oz tracks costs. No self-hosted tool does this well. | MEDIUM | Host-level metrics (CPU/memory) via daemon. Token spend requires parsing agent output or API tracking. |
| Config-driven skills and policies | Define what agents can and cannot do via configuration rather than code. Configurable autonomy levels per task type. RedMonk's 2025 research shows developers want fine-grained permissions. | MEDIUM | YAML/JSON policy files defining allowed operations, approval requirements, resource limits per agent type or task category. |
| Interactive session support (not just fire-and-forget) | Long-running sessions where operator can intervene, steer, provide input mid-run. GitHub Mission Control's mid-run steering. Most orchestrators are one-shot only. | HIGH | Requires bidirectional communication channel. Operator sends input to running agent session. Needs careful UX -- when does "steering" become "doing it yourself"? |
| Sanitization layer on agent inputs | Security-first approach to agent orchestration. No competitor emphasizes input sanitization. As agents get more autonomous, preventing prompt injection and command injection becomes critical. | MEDIUM | Filter/validate inputs before passing to agent CLIs. Configurable rules. Defense-in-depth alongside agent built-in sandboxing. |
| Web dashboard (Blazor) | Visual overview of fleet state, session status, host health. Conductor has a native Mac app. GitHub has Mission Control web UI. A web dashboard makes fleet status accessible from anywhere. | HIGH | Real-time updates via SignalR/SSE. Session list, host list, live output viewer. Second priority after CLI. |
| Cross-machine task coordination | Multiple agents working on related tasks across different machines, with awareness of each other. Microsoft AutoGen's collaborative agent pattern. Most orchestrators treat each agent as independent. | HIGH | Shared context, dependency tracking between sessions. Defer until core orchestration is solid. |

### Anti-Features (Commonly Requested, Often Problematic)

Features that seem good but create problems.

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|-----------------|-------------|
| Full container sandboxing from day one | Security! Isolation! | Massive complexity increase before core value is proven. Delays shipping by weeks/months. Agent CLIs already have built-in sandboxing (Claude Code has tool permissions, Codex runs sandboxed). | Start with agent built-in sandboxing + policy gating. Add container isolation incrementally as a pluggable backend. |
| Agent-to-agent direct communication | Agents collaborating on complex tasks sounds powerful. | Debugging distributed agent conversations is nightmarish. Emergent behaviors are unpredictable. No established patterns for coding agent collaboration that work reliably. | Orchestrator-mediated coordination. Operator defines task dependencies, coordinator sequences execution. Agents don't talk to each other directly. |
| Auto-merge of agent PRs | Fully autonomous end-to-end pipeline. | Merging untested, unreviewed AI code automatically is how you ship bugs. Even Devin requires human PR review. The industry consensus is human-in-the-loop for merges. | Auto-create PRs. Notify operator. Provide diff review. Operator merges. |
| Natural language task decomposition | "Build feature X" and the system breaks it into sub-tasks automatically. | Task decomposition quality is unreliable. Bad decomposition wastes more agent time than manual decomposition. The orchestrator becomes an AI itself, adding another failure mode. | Operator decomposes tasks manually. System executes well-defined tasks reliably. Orchestrator is infrastructure, not AI. |
| Multi-tenant / multi-user auth | Enterprise feature request. | Single-operator use case first (per PROJECT.md). Multi-tenancy adds auth, RBAC, isolation, billing -- each a project in itself. Premature abstraction. | Single operator. Add multi-user later when there's actual demand. |
| Mobile-first UI | Monitor agents from your phone! | The primary workflow (reviewing diffs, steering agents, reading logs) needs screen real estate. Mobile is consumption-only at best. | Desktop-first web dashboard. Mobile-responsive as bonus, not a design driver. MAUI mobile later if needed. |
| Automatic tool installation on hosts | Register a host and auto-install missing agent CLIs. | Different machines have different environments, permissions, package managers. Auto-installation is fragile and a security concern. | Detect installed tools during registration. Report what's missing. Operator installs manually. |
| Real-time collaboration between operators | Multiple people watching/steering the same agent. | Single operator use case (per PROJECT.md). Adds WebSocket complexity, conflict resolution, presence indicators. | Single operator dashboard. Share session logs/links for async review. |

## Feature Dependencies

```
[Host Registration & Inventory]
    +--requires--> [Host Agent Daemon]
    +--enables---> [Session Launch]
                       +--requires--> [Agent CLI Adapters]
                       +--requires--> [Git Worktree Management]
                       +--enables---> [Real-time Output Streaming]
                       |                  +--enables---> [Web Dashboard Live View]
                       +--enables---> [Session Lifecycle (stop/status)]
                       +--enables---> [Session History & Logs]

[Policy/Skill Configuration]
    +--enables---> [Approval/Elevation Flow]
    +--enables---> [Sanitization Layer]

[Session Launch] + [Policy Configuration]
    +--enables---> [Interactive Sessions (bidirectional)]

[Host Agent Daemon]
    +--enables---> [Resource Usage Monitoring]

[CLI Interface]
    +--enables---> [Web Dashboard] (second UI, same API)
                       +--enables---> [MAUI Client] (third UI)

[Pluggable Execution Backend]
    +--SSH Backend] (simplest, first)
    +--Nomad Backend] (better scheduling, second)
    +--Container Backend] (future)
```

### Dependency Notes

- **Session Launch requires Host Agent Daemon:** Something on the remote machine must receive and execute commands. This is the critical infrastructure piece.
- **Session Launch requires Agent CLI Adapters:** Each agent type (Claude, Codex, Copilot) has different CLI syntax, output format, and interaction model. Adapters normalize these differences.
- **Git Worktree Management requires Session Launch context:** Worktrees are created per-session, so session creation drives worktree lifecycle.
- **Approval Flow requires Policy Configuration:** Without policies defining what needs approval, there is nothing to gate.
- **Web Dashboard requires CLI/API to exist first:** The dashboard is a visualization of the same API the CLI uses. Build API-first, CLI as first consumer, dashboard second.
- **Interactive Sessions conflict with fire-and-forget simplicity:** Supporting both models adds complexity. Build fire-and-forget first, add interactive later.

## MVP Definition

### Launch With (v1)

Minimum viable product -- what's needed to validate "see and control agents across machines."

- [ ] Host registration with agent tool discovery -- register machines, know what's installed
- [ ] Host agent daemon (lightweight, receives commands) -- the "agent on the machine" that runs things
- [ ] Session launch via SSH to registered hosts -- execute agent CLI commands remotely
- [ ] Agent CLI adapters for Claude Code and one other agent -- prove multi-agent support
- [ ] Git worktree creation per session -- isolation from first session
- [ ] Real-time output streaming (SSE) -- see what agents are doing
- [ ] Session lifecycle (start, monitor, stop, force-kill) -- basic control
- [ ] Session history with stored output -- review past sessions
- [ ] CLI client as primary interface -- operator's daily driver
- [ ] Coordinator API (REST) with session CRUD and host listing -- foundation for all UIs

### Add After Validation (v1.x)

Features to add once core orchestration works reliably across machines.

- [ ] Additional agent adapters (Codex, Copilot, Gemini, OpenCode) -- expand coverage when adapter pattern is proven
- [ ] Diff review workflow (view agent changes, approve/reject) -- before this, operator reviews via git manually
- [ ] Policy/skill configuration (YAML-based) -- define what agents can do
- [ ] Approval/elevation flow for gated actions -- human-in-the-loop for destructive operations
- [ ] Resource usage visibility (CPU, memory from host daemon) -- cost and load awareness
- [ ] Web dashboard (Blazor) with real-time session views -- visual fleet overview
- [ ] Notification system (session complete, approval needed, errors) -- don't make operator poll
- [ ] Sanitization layer on agent inputs -- security hardening

### Future Consideration (v2+)

Features to defer until core platform is stable and in daily use.

- [ ] Interactive session support (bidirectional steering) -- complex, needs proven streaming infrastructure first
- [ ] Nomad execution backend -- better scheduling and isolation, but SSH works for 5-10 machines
- [ ] Container-based execution environments -- incremental sandboxing improvement
- [ ] Cross-machine task coordination -- agents aware of each other's work
- [ ] MAUI desktop/mobile client -- third UI surface
- [ ] Token spend tracking per session -- requires agent-specific output parsing or API integration
- [ ] Blob-backed artifact/snapshot storage -- needed for larger workflows
- [ ] MCP protocol support for agent control -- alongside direct CLI, not instead of

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| Host registration + agent discovery | HIGH | LOW | P1 |
| Host agent daemon | HIGH | HIGH | P1 |
| Session launch via SSH | HIGH | MEDIUM | P1 |
| Real-time output streaming (SSE) | HIGH | MEDIUM | P1 |
| Session lifecycle (start/stop/status) | HIGH | LOW | P1 |
| Agent CLI adapters (Claude + 1 other) | HIGH | MEDIUM | P1 |
| Git worktree per session | HIGH | LOW | P1 |
| CLI client | HIGH | MEDIUM | P1 |
| Coordinator REST API | HIGH | MEDIUM | P1 |
| Session history/logs | MEDIUM | LOW | P1 |
| Additional agent adapters | MEDIUM | MEDIUM | P2 |
| Diff review workflow | HIGH | LOW | P2 |
| Policy/skill config (YAML) | MEDIUM | MEDIUM | P2 |
| Approval/elevation flow | MEDIUM | HIGH | P2 |
| Resource usage monitoring | MEDIUM | MEDIUM | P2 |
| Web dashboard (Blazor) | HIGH | HIGH | P2 |
| Notification system | MEDIUM | MEDIUM | P2 |
| Sanitization layer | MEDIUM | MEDIUM | P2 |
| Interactive sessions | MEDIUM | HIGH | P3 |
| Nomad backend | LOW | HIGH | P3 |
| Container isolation | LOW | HIGH | P3 |
| Cross-machine coordination | MEDIUM | HIGH | P3 |
| MAUI client | LOW | HIGH | P3 |
| Token spend tracking | LOW | MEDIUM | P3 |
| MCP protocol support | LOW | MEDIUM | P3 |

**Priority key:**
- P1: Must have for launch -- proves the core value proposition
- P2: Should have, add when core is working -- makes the tool production-quality
- P3: Nice to have, future consideration -- expands capability once foundation is solid

## Competitor Feature Analysis

| Feature | Claude Squad | Conductor | Warp Oz | GitHub Mission Control | Devin | AgentSafeEnv (Our Approach) |
|---------|-------------|-----------|---------|----------------------|-------|----------------------------|
| Multi-machine fleet | No (single machine, tmux) | No (single Mac app) | Yes (cloud-hosted) | Yes (GitHub-hosted) | Yes (cloud-hosted) | Yes (self-hosted, operator-controlled) |
| Agent type support | Claude, Aider, Codex, OpenCode, Amp | Claude Code, Codex | Custom agents via Docker | Copilot only | Devin only | Claude, Copilot, Codex, Gemini, OpenCode |
| Git worktree isolation | Yes | Yes | Docker environments | Ephemeral environments | Sandboxed VM | Yes |
| Real-time monitoring | Terminal view (tmux) | Dashboard (Mac app) | Web dashboard + CLI | Web UI (Mission Control) | Web chat + Slack | SSE streaming + CLI + Web dashboard |
| Human-in-the-loop | Manual (switch to session) | Review diffs | Steer running tasks | Mid-run steering | Chat-based feedback | Policy-driven approval gates |
| Self-hosted | Yes (local tool) | No (Mac app) | Optional (enterprise) | No (GitHub SaaS) | No (SaaS) | Yes (primary deployment model) |
| Execution backend | tmux | Native process | Docker containers | GitHub Actions | Proprietary VM | Pluggable (SSH, Nomad, containers) |
| Pricing | Free/OSS | Paid app | Paid SaaS | GitHub subscription | Per-ACU pricing | Self-hosted (free to run) |

**Key competitive position:** AgentSafeEnv fills the gap of a self-hosted, multi-machine, multi-agent orchestrator. Claude Squad and Conductor are local-only. Warp Oz, GitHub Mission Control, and Devin are cloud/SaaS. Nobody offers a self-hosted fleet orchestrator supporting multiple agent types across multiple machines with pluggable execution backends.

## Sources

- [Conductors to Orchestrators: The Future of Agentic Coding (Addy Osmani / O'Reilly)](https://www.oreilly.com/radar/conductors-to-orchestrators-the-future-of-agentic-coding/)
- [Claude Squad GitHub](https://github.com/smtg-ai/claude-squad)
- [Conductor - Run a team of coding agents](https://www.conductor.build/)
- [Warp Oz: Orchestration Platform for Cloud Agents](https://www.warp.dev/oz)
- [GitHub Mission Control for Copilot agent orchestration](https://github.blog/ai-and-ml/github-copilot/how-to-orchestrate-agents-using-mission-control/)
- [Devin AI documentation](https://docs.devin.ai/)
- [21 agent orchestration tools for managing your AI fleet (CIO)](https://www.cio.com/article/4138739/21-agent-orchestration-tools-for-managing-your-ai-fleet.html)
- [10 Things Developers Want from Agentic IDEs 2025 (RedMonk)](https://redmonk.com/kholterhoff/2025/12/22/10-things-developers-want-from-their-agentic-ides-in-2025/)
- [Human-in-the-Loop for AI Agents (Permit.io)](https://www.permit.io/blog/human-in-the-loop-for-ai-agents-best-practices-frameworks-use-cases-and-demo)
- [Okteto AI Agent Fleets](https://www.okteto.com/blog/run-ai-agents-at-scale-with-okteto-agent-fleets/)
- [Gartner Multiagent Orchestration Platforms Reviews](https://www.gartner.com/reviews/market/multiagent-orchestration-platforms)

---
*Feature research for: Multi-agent coding orchestration platform*
*Researched: 2026-03-08*
