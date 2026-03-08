# Pitfalls Research

**Domain:** Multi-agent orchestration platform (CLI-wrapping AI coding agents across remote machines)
**Researched:** 2026-03-08
**Confidence:** HIGH (corroborated across multiple sources, verified against existing scaffold)

## Critical Pitfalls

### Pitfall 1: Treating Agent CLIs as Stable APIs

**What goes wrong:**
Agent CLIs (Claude, Codex, Gemini, Copilot, OpenCode) change their output format, flags, and behavior between versions without warning. A wrapper that parses stdout text today breaks silently tomorrow when the agent updates. The platform stops understanding agent output but keeps running, producing garbage data in the dashboard.

**Why it happens:**
CLI tools are designed for humans, not machines. Developers build parsers against current output and assume stability. Agent CLIs are especially volatile -- they ship weekly updates, change JSON schemas, add/remove flags, and alter streaming behavior. Claude Code, Codex, and others have all changed output formats multiple times in 2025-2026.

**How to avoid:**
- Use `--output-format json` / `--json` flags where available (Claude CLI, Codex CLI both support these). Treat structured output as the only supported mode.
- Build an adapter layer per agent type with version detection. Each adapter owns parsing for one agent CLI and declares which versions it supports.
- Never parse human-readable stdout. If an agent CLI lacks machine-readable output, wrap it in a shim that captures exit codes and raw output without interpretation.
- Pin agent CLI versions on managed hosts and test against new versions before upgrading.
- Design the `SkillExecDefinition` model (already in scaffold) to include an `OutputFormat` field specifying expected output structure.

**Warning signs:**
- Tests pass locally but sessions produce garbled events on a host with a newer agent version
- `SessionEvent` data fields contain unparseable strings
- Agent type X suddenly shows all sessions as "Failed" after a host update

**Phase to address:**
Phase 1 (Core Backend) -- the adapter/driver layer must be version-aware from day one. This is foundational; retrofitting version tolerance into a monolithic parser is a rewrite.

---

### Pitfall 2: Losing Process Lifecycle Control on Remote Hosts

**What goes wrong:**
An agent session is launched on a remote machine via SSH or Nomad, then the coordinator loses contact (network blip, coordinator restart, host reboot). The agent process keeps running unsupervised -- consuming tokens, making changes to code, burning API credits -- with no way to stop it. Alternatively, the SSH connection drops and the agent process is killed by SIGHUP before completing its task.

**Why it happens:**
Remote process management is fundamentally hard. SSH sessions tie process lifetime to connection lifetime by default. Developers assume "start process" and "monitor process" are the same operation, but they are two separate concerns that must be independently resilient.

**How to avoid:**
- On remote hosts, always launch agents inside `tmux`/`screen` or via `nohup`/`setsid` so they survive SSH disconnection.
- Deploy a lightweight host-side daemon (agent runner) that owns the process lifecycle locally. The coordinator talks to the daemon, not directly to the agent process. If the coordinator disconnects, the daemon keeps the agent alive (or kills it based on policy).
- Implement heartbeat/watchdog: the host daemon expects periodic heartbeats from the coordinator. If heartbeats stop for N seconds, apply the session's timeout policy (kill after grace period, or keep running with capped duration).
- Store session state in the database, not just in-memory (`ConcurrentDictionary` in the current scaffold). On coordinator restart, reconcile DB state against actual host state.
- The current `SshBackend` stores sessions in `ConcurrentDictionary<string, SessionSummary>` -- this is a critical gap. A coordinator restart loses all session tracking.

**Warning signs:**
- After coordinator restart, `ListSessionsAsync` returns empty but agents are still running on hosts
- SSH sessions that should have timed out are still consuming resources hours later
- "Zombie" agent processes accumulating on hosts over days

**Phase to address:**
Phase 1 (Core Backend) for the host daemon design. Phase 2 (SSH Backend) for the actual implementation. The in-memory session store must be replaced with persistent storage before any real SSH execution.

---

### Pitfall 3: SSE Event Stream Gaps Causing Silent Data Loss

**What goes wrong:**
The coordinator streams agent events to the UI via SSE. The client disconnects momentarily (tab switch on mobile, network transition, laptop sleep). When it reconnects, events emitted during the gap are lost forever. The user sees a jump in agent output with no indication that content was missed. Worse: state-change events are lost, so the UI shows "Running" when the session already completed.

**Why it happens:**
SSE without event IDs and replay capability is fire-and-forget. The default implementation just writes to the response stream. Developers test on localhost with perfect connectivity and never see the gap.

**How to avoid:**
- Assign monotonically increasing IDs to every `SessionEvent`. The scaffold's `SessionEvent` record needs an `EventId` (long or ULID) field.
- Buffer recent events per session (ring buffer, last N events or last T seconds). On reconnect, replay from `Last-Event-ID` header.
- .NET 10 provides native `SseItem<T>` with ID and retry interval support -- use it instead of raw `Response.WriteAsync`.
- Implement a `retry:` field in SSE to control client reconnection timing (default is 3 seconds, which is fine for most cases).
- For the CLI client, implement explicit reconnection with event ID tracking rather than relying on browser behavior.
- Cap the event buffer size to prevent memory exhaustion on long-running sessions.

**Warning signs:**
- QA reports "output jumped" or "missed what the agent did"
- Session state in UI disagrees with actual session state
- No `Last-Event-ID` handling in the SSE endpoint

**Phase to address:**
Phase 1 (Core Backend) -- event ID assignment and buffering must be in the event model from the start. Retrofitting IDs into an existing event stream is painful because consumers already assume event ordering.

---

### Pitfall 4: Git Worktree Contention and Resource Exhaustion

**What goes wrong:**
Multiple agents working on the same repo via git worktrees hit branch locking conflicts, shared index corruption, or disk exhaustion. Git only allows one worktree per branch. Two agents targeting the same branch silently fail. On a 2GB codebase, 5 concurrent worktrees consume 10GB of disk. `node_modules`, build artifacts, and `.env` files don't exist in new worktrees, causing agent tasks to fail with cryptic errors.

**Why it happens:**
Git worktrees share the `.git` directory. Concurrent git operations (checkout, commit, rebase) can deadlock on the shared index lock. Developers test with one agent at a time and never see contention. The scaffold's `GitWorktreeProvider` and `WorktreeDescriptor` model the happy path but don't address these failure modes.

**How to avoid:**
- Always use detached HEAD or unique branch names per worktree -- never share branches between concurrent agents.
- Implement worktree lifecycle management: create on session start, clean up on session end, with a cleanup sweeper for orphaned worktrees.
- Use shallow + sparse clones (the scaffold's `WorktreeDescriptor` already has `Shallow` and `Sparse` fields -- good) to limit disk usage.
- Pre-validate disk space before creating worktrees. Set per-host worktree limits.
- For projects needing `npm install` or similar setup, define a "worktree init" hook in the skill/session config.
- Implement file locking awareness: if git operations fail with lock errors, retry with backoff rather than failing the session.

**Warning signs:**
- Sessions fail with "fatal: '[branch]' is already checked out at '[path]'"
- Disk usage on agent hosts grows monotonically over days
- Git operations intermittently fail with ".git/index.lock" errors

**Phase to address:**
Phase 2 (Source Sharing) -- but the `WorktreeDescriptor` model should include cleanup policy fields from Phase 1.

---

### Pitfall 5: Blacklist-Based Sanitization Creates False Security

**What goes wrong:**
The scaffold's `BasicSanitizationService` uses a blocklist of dangerous patterns (`rm -rf /`, `del /f /s`, etc.). This catches the literal strings but misses trivially obfuscated variants (`r\u006D -rf /`, `$(echo rm) -rf /`, base64-encoded commands, variable expansion). Operators trust the sanitization layer and grant agents more permissions than they should.

**Why it happens:**
Blocklist-based security is easy to implement and easy to demo. It catches the examples you thought of. But shell command injection has infinite variants -- every new shell feature is a potential bypass. The approach creates a false sense of security that is more dangerous than having no sanitization at all (because without it, you'd be more cautious about what you allow).

**How to avoid:**
- Treat the sanitization layer as defense-in-depth, never as the primary security boundary.
- The primary security boundary is the execution environment: run agents in sandboxed contexts where dangerous operations are impossible at the OS level (containers, unprivileged users, restricted filesystem mounts).
- If keeping the blocklist approach for early phases (acceptable for MVP), explicitly document it as "advisory only, not a security boundary" and do not gate permissions on it.
- Move toward allowlist-based execution: agents can only run commands defined in `SkillManifest` entries, not arbitrary shell input. The scaffold already has this pattern (skills with `PlatformCommand` definitions) -- enforce it.
- For the incremental sandboxing plan (agent built-in -> policy gating -> containers), make sure each layer is clearly labeled with its actual security guarantees.

**Warning signs:**
- Security reviews or penetration tests bypass the sanitizer trivially
- Team members refer to the sanitizer as "the security layer"
- Raw input mode is enabled for SSH sessions without additional containment

**Phase to address:**
Phase 1 (Core Backend) -- establish the correct mental model early. The scaffold's sanitization code is fine to keep as advisory logging, but must not be trusted as a security gate. Container-based isolation should be planned for Phase 3+.

---

### Pitfall 6: Coordinator as Single Point of Failure and Bottleneck

**What goes wrong:**
All agent sessions route through a single coordinator instance. The coordinator holds session state in memory, manages SSE connections, and proxies all communication. When it restarts (deploy, crash, OOM), every active session loses its event stream, every UI client disconnects, and session state is lost. At 10+ concurrent sessions with streaming output, the coordinator becomes CPU/memory-bound on event marshaling.

**Why it happens:**
Centralized orchestration is the natural first architecture. The scaffold's `SessionCoordinator` holds all session logic. Developers don't hit scaling limits until real agents are producing real output volumes.

**How to avoid:**
- Persist session metadata to the database (EF Core) from the start, not just in-memory dictionaries. The coordinator should be stateless except for active SSE connections.
- Design the event bus as a separate concern (even if in-process initially). Events flow: Host Daemon -> Event Bus -> Coordinator -> SSE Clients. This allows the coordinator to restart without losing events if the bus is durable.
- For Phase 1 MVP, a single coordinator is fine, but structure the code so session state queries hit the DB and event streaming is decoupled from session management.
- Plan for horizontal scaling later: multiple coordinator instances sharing a database and event bus (Redis Streams, or a persistent queue).

**Warning signs:**
- Coordinator memory usage grows linearly with active sessions and never drops
- Coordinator restart requires manually re-checking all host agent states
- Deployment of the coordinator causes visible disruption to all users

**Phase to address:**
Phase 1 (Core Backend) for stateless coordinator design. Phase 3+ for actual horizontal scaling. Getting the abstractions right early prevents a rewrite.

---

### Pitfall 7: Agent-Specific Behavior Differences Hidden Behind a Uniform Interface

**What goes wrong:**
The platform abstracts all agents behind `ISessionBackend` and `SkillManifest`, suggesting they work the same way. But Claude Code has interactive approval flows, Codex runs in sandboxed containers by default, Copilot operates as an IDE extension (not a standalone CLI), Gemini has different authentication models, and OpenCode has its own session semantics. Building features against the abstraction without testing each agent leads to "works with Claude, broken with everything else."

**Why it happens:**
Clean abstractions feel productive. The scaffold's interfaces look elegant. But each agent CLI has fundamentally different interaction models:
- Claude Code: interactive REPL, permission prompts, `--yes` flag for auto-approval
- Codex CLI: sandbox-first, operates on worktrees by default, returns structured diffs
- GitHub Copilot CLI: suggestions-only, not a persistent session
- Gemini CLI: different auth flow (Google Cloud credentials)
- OpenCode: different session lifecycle semantics

**How to avoid:**
- Build and test one agent adapter at a time. Ship Claude Code support first (most capable, most documented, best machine-readable output). Add agents one at a time in subsequent phases.
- Document per-agent capability matrices: which agents support interactive sessions, which are fire-and-forget, which need approval flows, which provide structured output.
- The `ISessionBackend` interface should have capability flags (e.g., `SupportsInteractiveInput`, `SupportsStructuredOutput`, `RequiresApprovalFlow`) so the coordinator can adapt behavior.
- Accept that some agents may only support a subset of platform features. A Copilot "session" will look very different from a Claude Code session.

**Warning signs:**
- All tests use a single agent type (InMemoryBackend or one real agent)
- Feature PRs say "works with Claude" without testing others
- Users report "this works with Claude but not with Codex" after launch

**Phase to address:**
Phase 1 should support only one real agent (Claude Code). Phase 2 adds a second agent. Do not attempt to support all 5+ agents simultaneously in any single phase.

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| In-memory session store (`ConcurrentDictionary`) | Fast to implement, no DB setup | All state lost on restart, no audit trail, no multi-instance | Never in production. Replace in Phase 1 with EF Core persistence |
| Hardcoded host inventory in `SshBackend` | Unblocks development | Cannot add/remove hosts without code changes, no dynamic discovery | Only during initial scaffold exploration. Replace with config/DB-driven registry in Phase 1 |
| Regex-based sanitization blocklist | Quick security theater for demos | False sense of security, trivially bypassable, maintenance burden growing with every new pattern | Acceptable as advisory logging layer alongside real isolation. Never as sole security |
| Sequential backend polling in `ListSessionsAsync` | Simple code | O(N) backend calls serialized. With 3 backends and network latency, listing sessions takes seconds | Acceptable for 1-2 backends. Parallelize (`Task.WhenAll`) before adding third backend |
| Single `Func<SessionEvent, Task> emit` callback | Clean API surface | No fan-out to multiple consumers, no persistence, no replay. Tight coupling between event production and consumption | Acceptable for Phase 1 MVP if events are also persisted via a decorator/middleware |
| Monolithic coordinator class | All logic in one place, easy to understand | Becomes a god class. Session lifecycle, event routing, sanitization, policy checks all entangled | Acceptable through Phase 1 if responsibilities are clearly separated into private methods ready for extraction |

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| SSH to remote hosts | Assuming SSH connection lifetime = agent process lifetime | Launch agent in `tmux`/`screen`/`nohup` on remote. SSH is for command dispatch, not process hosting |
| Claude Code CLI | Forgetting `--yes` flag causes agent to block on permission prompts indefinitely | Always pass `--yes` (or equivalent auto-approve) for automated sessions. Log what was auto-approved for audit |
| Codex CLI | Assuming Codex works in current directory like Claude | Codex creates its own sandbox/worktree. Work with its model, not against it |
| Git worktrees | Running `git` commands from the worktree that hit the shared `.git` dir concurrently | Serialize git operations per-repo (not per-worktree) or use `--no-optional-locks` for read operations |
| SSE via reverse proxy (IIS/nginx/Azure) | Proxy buffers SSE responses, client sees nothing for minutes then a burst | Set `X-Accel-Buffering: no`, `Cache-Control: no-cache`, disable response buffering at every layer |
| Nomad job submission | Submitting jobs synchronously and blocking on completion | Submit job, store allocation ID, poll/watch for status changes asynchronously |
| EF Core with SQLite | Concurrent writes from multiple threads/sessions | Use WAL mode, single writer with queue, or switch to Postgres for concurrent workloads |
| Agent CLI version detection | Hardcoding expected `--version` output format | Use semver parsing with fallback. Some CLIs output `v1.2.3`, others `1.2.3`, others `tool version 1.2.3` |

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| Buffering entire agent stdout in memory | OOM on coordinator during long sessions | Stream events to DB/disk incrementally, keep only recent N events in memory | Sessions exceeding ~10 minutes of continuous output (~50MB+ of text) |
| Polling all backends sequentially for session list | UI dashboard takes 3-5 seconds to load | `Task.WhenAll` for parallel backend queries, cache inventory for 30-60 seconds | More than 2 backends registered |
| SSE connections holding threads | Thread pool exhaustion under concurrent viewers | ASP.NET Core async SSE (default with Kestrel) handles this correctly, but avoid synchronous writes or blocking calls inside the SSE loop | 50+ concurrent SSE connections with synchronous middleware |
| No event batching for high-volume agents | Thousands of tiny SSE messages per second, browser/CLI client lags | Batch events by time window (e.g., flush every 100ms) for display, keep raw events for persistence | Agents producing >100 events/second (common with verbose stdout) |
| Git worktree creation per session | Disk full on hosts running many sessions | Pool worktrees, reuse after cleanup, set per-host limits, use shallow clones | 5+ concurrent sessions on a host with a large repo |

## Security Mistakes

| Mistake | Risk | Prevention |
|---------|------|------------|
| Passing user input directly as CLI arguments to agent processes | Command injection -- attacker crafts input that escapes the intended command | Use argument arrays (never string interpolation for commands). The scaffold's `PlatformCommand` with `File` and `Args` array is correct -- enforce this pattern everywhere |
| Storing agent API keys/tokens in session metadata or events | Credential leakage via API, logs, or SSE stream | Agent credentials live on the host only, never transit through the coordinator. The coordinator dispatches "run this skill" not "here's the API key" |
| SSH keys with no passphrase and broad host access | Compromise of coordinator = compromise of all hosts | Use per-host SSH keys with restricted authorized_keys (forced command), or prefer Nomad (which has its own auth) over raw SSH |
| Agent auto-approve (`--yes`) without scope limits | Agent can execute any tool, install packages, make network requests without human review | Combine `--yes` with agent-level allowlists (Claude's `--allowedTools`, Codex's permission model). Log all auto-approved actions |
| Trusting agent output for control flow decisions | Agent says "task complete" but actually failed. Prompt injection in codebase causes agent to report false status | Verify agent outcomes independently (check git diff, run tests, verify file changes) rather than trusting agent self-reports |

## UX Pitfalls

| Pitfall | User Impact | Better Approach |
|---------|-------------|-----------------|
| Showing raw agent stdout without formatting | Unreadable wall of text mixing ANSI codes, markdown, tool calls, and output | Parse agent output into structured segments (tool use, thinking, output, error). Strip or render ANSI codes appropriately per client |
| No indication of event stream gaps | User thinks they saw everything but missed critical agent actions | Show explicit "reconnected, replaying N missed events" indicator. Never silently resume |
| Requiring users to pick hosts manually | Cognitive overhead, wrong host selection | Default to automatic placement (the scaffold's `IPlacementEngine`). Manual host selection as advanced override only |
| Single-agent view only | User must click through sessions one at a time to understand fleet state | Provide a dashboard/summary view showing all active sessions at a glance with status, host, and last event |
| No session cost visibility | Surprise API bills from long-running or runaway agents | Show estimated token usage / API cost per session in real-time. Alert on cost thresholds |

## "Looks Done But Isn't" Checklist

- [ ] **Session Start:** Often missing timeout configuration -- verify every session has a max duration, and the host daemon enforces it
- [ ] **SSE Streaming:** Often missing reconnection handling -- verify `Last-Event-ID` is sent, processed, and events replayed
- [ ] **Agent Adapters:** Often missing version compatibility checks -- verify the adapter detects agent CLI version and warns on untested versions
- [ ] **Host Registration:** Often missing health checks -- verify hosts are periodically probed for availability, not just trusted from initial registration
- [ ] **Worktree Cleanup:** Often missing orphan detection -- verify worktrees are cleaned up when sessions end abnormally (crash, host reboot)
- [ ] **Sanitization:** Often missing bypass detection -- verify the sanitizer is tested with obfuscated variants, not just literal dangerous strings
- [ ] **Error Propagation:** Often missing structured error events -- verify agent failures produce `SessionEvent` entries, not just swallowed exceptions
- [ ] **Graceful Shutdown:** Often missing drain logic -- verify coordinator shutdown waits for active SSE connections to receive a "shutting down" event before closing

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| Lost session state (coordinator crash) | MEDIUM | Query each host daemon for running processes, reconcile against DB, recreate session records. Lost events cannot be recovered if not persisted |
| Runaway agent process | LOW | Host daemon kills process by PID. If no daemon, SSH to host and kill manually. Add the session ID to a kill list for reconciliation |
| Git worktree corruption | MEDIUM | Delete the corrupted worktree directory, remove the worktree entry from git (`git worktree remove --force`), recreate. Agent must restart from last known good state |
| Agent CLI breaking change | HIGH | Roll back agent CLI version on affected hosts. Update the adapter layer. No shortcut -- parser must be fixed for new format |
| SSE event loss | LOW | Client reconnects with last known event ID. If events were persisted, replay works. If not persisted, accept the gap and show indicator to user |
| Disk exhaustion from worktrees | MEDIUM | Emergency cleanup of oldest/stopped session worktrees. Implement worktree quotas to prevent recurrence. May lose in-progress work |
| Sanitizer bypass | HIGH | Immediate: disable raw input mode, restrict to skill-only execution. Long-term: implement proper sandboxing. Audit all sessions that used raw input |

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| CLI API instability | Phase 1 (Core Backend) | Adapter layer exists per agent type with version detection; tests run against real CLI |
| Process lifecycle loss | Phase 1 (Core Backend) + Phase 2 (SSH) | Session state persisted to DB; host daemon deployed; coordinator restart does not orphan sessions |
| SSE event gaps | Phase 1 (Core Backend) | Events have IDs; reconnection replays missed events; integration test simulates disconnect |
| Git worktree contention | Phase 2 (Source Sharing) | Concurrent worktree creation test passes; cleanup runs on session end; disk quota enforced |
| False security from sanitizer | Phase 1 (Core Backend) | Sanitizer documented as "advisory"; no permissions gated solely on sanitizer approval; container isolation planned |
| Coordinator SPOF | Phase 1 (Core Backend) design, Phase 3+ implementation | Coordinator is stateless (DB-backed); deployment does not disrupt active sessions |
| Agent behavior differences | Phase 1 (one agent), Phase 2 (second agent) | Per-agent capability matrix documented; each adapter has its own test suite |

## Sources

- [Multi-agent workflows often fail. Here's how to engineer ones that don't](https://github.blog/ai-and-ml/generative-ai/multi-agent-workflows-often-fail-heres-how-to-engineer-ones-that-dont/) -- GitHub Engineering Blog
- [AI Agent Orchestration Patterns](https://learn.microsoft.com/en-us/azure/architecture/ai-ml/guide/ai-agent-design-patterns) -- Microsoft Azure Architecture Center
- [Designing Effective Multi-Agent Architectures](https://www.oreilly.com/radar/designing-effective-multi-agent-architectures/) -- O'Reilly Radar
- [CliWrap library](https://github.com/Tyrrrz/CliWrap) -- .NET process management best practices
- [Server-Sent Events in ASP.NET Core and .NET 10](https://www.milanjovanovic.tech/blog/server-sent-events-in-aspnetcore-and-dotnet-10) -- Milan Jovanovic
- [Codex App Worktrees Explained](https://www.verdent.ai/guides/codex-app-worktrees-explained) -- How parallel agents avoid git conflicts
- [Git Worktrees for AI Coding](https://dev.to/mashrulhaque/git-worktrees-for-ai-coding-run-multiple-agents-in-parallel-3pgb) -- Concurrent agent worktree patterns
- [Keep SSH Sessions Running After Disconnection](https://www.tecmint.com/keep-remote-ssh-sessions-running-after-disconnection/) -- SSH process lifecycle management
- [Claude Code CLI Reference](https://code.claude.com/docs/en/cli-reference) -- Machine-readable output flags
- [Codex CLI Reference](https://developers.openai.com/codex/cli/reference/) -- JSON output and automation flags
- [Agent Orchestration is Governance](https://medium.com/@markus_brinsa/agent-orchestration-orchestration-isnt-magic-it-s-governance-210afb343914) -- Compounding error in agent loops

---
*Pitfalls research for: AgentSafeEnv multi-agent orchestration platform*
*Researched: 2026-03-08*
