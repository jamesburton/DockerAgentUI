# Technology Stack

**Project:** AgentSafeEnv v1.1 -- Multi-Agent & Interactive
**Researched:** 2026-03-09
**Scope:** Stack additions/changes for v1.1 features ONLY (v1.0 stack validated and stable)

## Executive Decision

**No new NuGet packages required.** The v1.1 features are achievable entirely with the existing dependency set plus built-in .NET 10 / BCL capabilities. This is the strongest possible position -- zero new dependencies means zero new risk surface, no version conflicts, and no learning curve.

## Existing Stack (DO NOT CHANGE)

| Technology | Version | Status |
|------------|---------|--------|
| .NET 10 | 10.0 | Stable, LTS |
| ASP.NET Core | 10.0 | Stable |
| EF Core SQLite | 10.0.2 | Stable |
| SSH.NET | 2025.1.0 | Stable |
| MudBlazor | (current) | Stable |
| System.CommandLine | (current) | Stable |
| Aspire SDK | 9.2.1 | Stable |

## What Each v1.1 Feature Needs

### 1. Interactive Sessions (Bidirectional Steering, Pause/Resume)

**Claude Code CLI capabilities (verified via official docs):**

| CLI Flag | Purpose | How AgentHub Uses It |
|----------|---------|---------------------|
| `--continue` / `-c` | Continue most recent conversation | Resume paused sessions |
| `--resume` / `-r` | Resume specific session by ID/name | Resume specific sessions after pause |
| `--session-id` | Use a specific UUID for conversation | Track sessions across pause/resume cycles |
| `--input-format stream-json` | Accept NDJSON input stream | Bidirectional steering via stdin pipe |
| `--output-format stream-json` | Emit NDJSON output | Already used in v1.0 ClaudeCodeAdapter |
| `--fork-session` | Create new session from resumed point | Branch sessions for redirection |

**Stack additions needed: NONE.**

**Implementation approach using existing primitives:**

- `System.Threading.Channels` (already used in `DurableEventService`) -- use `Channel<T>` for bidirectional message passing between the API layer and the SSH stdin pipe
- Add `pause-session` and `resume-session` commands to `HostCommandProtocol` -- extends existing JSON-over-SSH-stdin protocol
- Add `SessionState.Paused` enum value to `Contracts.SessionState`
- The `ISshHostConnection.ExecuteCommandAsync` method already writes to SSH stdin -- pause/resume is a protocol extension, not a transport change
- `--session-id` flag lets AgentHub assign deterministic IDs and resume later with `--resume <id>`

**Why no SignalR / WebSocket:**
The existing SSE + stdin-over-SSH architecture already provides bidirectional communication. SSE delivers server-to-client events. `SendInputAsync` delivers client-to-server input via SSH stdin. Adding SignalR would create a parallel transport with no benefit. The v1.0 architecture already proved this pattern works for input/approval flows.

### 2. Multi-Agent Coordination (Task Dispatch, Sub-Agent Spawning, Resource-Aware Scheduling)

**Claude Code CLI capabilities (verified via official docs):**

| CLI Flag | Purpose | How AgentHub Uses It |
|----------|---------|---------------------|
| `--agents` | Define custom sub-agents via JSON | Dynamically configure agent specializations per session |
| `--agent` | Specify agent type for session | Route to preconfigured agent profiles |
| `--worktree` / `-w` | Isolate in git worktree | Parallel sessions on same repo (see section 4) |
| `--max-turns` | Limit agentic turns | Bound sub-agent execution |
| `--max-budget-usd` | Limit API spend | Budget guardrails per sub-agent |
| `--model` | Model selection (`sonnet`, `opus`, `haiku`) | Route sub-agents to cheaper models |
| `--tmux` | Launch in tmux session | Agent teams with split panes on Linux hosts |
| `--teammate-mode` | Agent team display mode | Control team coordination mode |

**Stack additions needed: NONE.**

**Implementation approach using existing primitives:**

- **Task dispatch:** Extend `IPlacementEngine` (already resource-aware with CPU/memory/GPU filtering in `SimplePlacementEngine`) to add load-based scoring instead of first-match. Add session-count-per-host tracking via DB query.
- **Sub-agent spawning:** Build `CoordinationService` that starts child sessions via the existing `ISessionCoordinator.StartSessionAsync` path. Each sub-agent is a regular SSH session with `--agents` JSON passed to configure specialization.
- **Resource-aware scheduling:** `HostMetricPollingService` already collects CPU/memory every 30s. Extend `NodeCapability` to include `ActiveSessionCount` and `DiskFreeMb`. The `IPlacementEngine.ChooseNode` signature already accepts `IReadOnlyList<NodeCapability>` -- add fields, update scoring.
- **Parent-child session tracking:** Add `ParentSessionId` nullable FK to `SessionEntity`. Query children via EF Core.
- **Coordination events:** Add `SessionEventKind.SubAgentSpawned`, `SubAgentCompleted` to the existing enum. Route via `DurableEventService.EmitAsync`.

**Why no external message queue (RabbitMQ, Redis Streams):**
The system orchestrates 5-10 hosts with 2-5 concurrent agents per host. `DurableEventService` with SQLite persistence already handles event routing at this scale. A message queue adds operational complexity (another service to deploy) with no throughput benefit. The existing Channel<T> + SSE architecture handles fan-out. Revisit only if scaling past 50+ concurrent sessions.

### 3. Host Inventory (Agent CLI Discovery, Version Detection, Tool Enumeration)

**Claude Code CLI capabilities (verified via official docs):**

| CLI Flag/Command | Purpose | How AgentHub Uses It |
|-----------------|---------|---------------------|
| `claude -v` | Output version number | Version detection |
| `claude agents` | List configured sub-agents | Tool/agent enumeration |
| `claude auth status` | Authentication status JSON | Health check |
| `claude mcp` | MCP server configuration | Tool enumeration |

**Stack additions needed: NONE.**

**Implementation approach using existing primitives:**

- **Discovery commands:** SSH to host, run `which claude`, `claude -v`, `which codex`, `codex --version`. Parse stdout. This is identical to how `HostMetricPollingService` already runs OS commands via `ISshHostConnectionFactory`.
- **Inventory polling:** Create `HostInventoryPollingService : BackgroundService` following the same pattern as `HostMetricPollingService` -- periodic SSH execution with DB persistence.
- **DB schema:** Add `HostInventoryEntity` with columns: `HostId`, `AgentType`, `CliPath`, `Version`, `ToolsJson`, `DiskFreeMb`, `LastScannedUtc`. Standard EF Core migration.
- **Disk space:** Add `df -m /` (Linux) / `Get-PSDrive C` (Windows) to the metric/inventory SSH commands.
- **Health checks:** Extend the existing `HostMetricPollingService` polling cycle or run a separate lower-frequency inventory scan (every 5 minutes vs 30s for metrics).

**SSH command set for Claude Code inventory:**
```bash
# Version detection
claude --version 2>/dev/null && echo "claude-code:$(claude --version)"

# Installed agent CLIs
which claude codex cursor 2>/dev/null

# Disk space (Linux)
df -m / | tail -1 | awk '{print $4}'

# Git version (for worktree support detection)
git --version 2>/dev/null
```

### 4. Git Worktree Isolation (`--worktree` Flag for Claude Code)

**Claude Code CLI capabilities (verified via official docs):**

| CLI Flag | Purpose | How AgentHub Uses It |
|----------|---------|---------------------|
| `--worktree` / `-w` | Start in isolated git worktree | One worktree per agent session |
| `--worktree <name>` | Named worktree | Deterministic worktree naming for tracking |

**Worktree behavior (from official docs):**
- Creates worktree at `<repo>/.claude/worktrees/<name>`
- Each worktree gets its own checkout of a branch
- On exit with no changes: auto-cleanup (worktree + branch removed)
- On exit with changes: prompts to keep or remove
- Shares git history/objects/config with parent repo

**Stack additions needed: NONE.**

**Implementation approach using existing primitives:**

- **Existing foundation:** `GitWorktreeProvider` already exists at `src/AgentHub.Orchestration/Storage/GitWorktreeProvider.cs`. `WorktreeDescriptor` record exists in Contracts. `SessionRequirements.WorktreeId` field exists. `SessionSummary.WorktreePath` field exists.
- **What's missing:** The current `GitWorktreeProvider.EnsureMaterializedAsync` is a stub -- it creates a directory and writes a text file but does not run `git worktree add`. The real implementation needs to either (a) execute `git worktree add` via SSH on the remote host, or (b) pass `--worktree <name>` to Claude Code and let it handle worktree creation.
- **Preferred approach (b):** Pass `--worktree <name>` to the Claude CLI via `ClaudeCodeAdapter.BuildCommandArgs`. Claude Code handles all git mechanics internally. AgentHub just tracks the worktree name and monitors cleanup.
- **ClaudeCodeAdapter change:** Add `-w <worktreeId>` to `BuildCommandArgs` when `request.WorktreeId` is set. Approximately 3 lines of code.
- **Cleanup tracking:** On session stop, check if worktree still exists via SSH (`git worktree list`). Track state in `SessionEntity`.

**Why not manage worktrees from the coordinator side:**
Claude Code's `--worktree` flag handles all the git mechanics (branch creation, checkout, cleanup prompts). AgentHub should pass the flag and track the worktree name, not reimplement git worktree management. The remote host has git installed; the coordinator does not need to.

## Recommended Stack (Complete v1.1)

### Core Framework (UNCHANGED)
| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| .NET 10 | 10.0 | Runtime | LTS, non-negotiable |
| ASP.NET Core | 10.0 | Web API + SSE | Already proven in v1.0 |
| EF Core SQLite | 10.0.2 | Persistence | Already proven, schema extensions only |

### Communication (UNCHANGED)
| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| SSH.NET | 2025.1.0 | Remote execution | SSH stdin/stdout bidirectional, proven |
| System.Threading.Channels | (BCL) | In-process async queues | Already used in DurableEventService |
| SSE (built-in) | (BCL) | Server-to-client events | Already proven for real-time streaming |

### Infrastructure (UNCHANGED)
| Technology | Version | Purpose | Why |
|------------|---------|---------|-----|
| Aspire SDK | 9.2.1 | Local dev orchestration | Non-negotiable |
| MudBlazor | (current) | Dashboard UI | Non-negotiable |
| System.CommandLine | (current) | CLI client | Non-negotiable |

### New Internal Abstractions (NO NEW PACKAGES)
| Abstraction | Location | Purpose | Uses |
|-------------|----------|---------|------|
| `ICoordinationService` | Orchestration | Multi-agent task dispatch | ISessionCoordinator, IPlacementEngine |
| `IHostInventoryService` | Orchestration | Agent CLI discovery | ISshHostConnectionFactory |
| `HostInventoryPollingService` | Orchestration/Monitoring | Periodic inventory scan | BackgroundService pattern |
| `SessionState.Paused` | Contracts | Pause/resume support | Enum extension |
| Extended `HostCommandProtocol` | Orchestration/HostDaemon | pause/resume/inventory commands | JSON protocol extension |

## Claude Code CLI Integration Matrix

**Confidence: HIGH** -- all flags verified against official docs at https://code.claude.com/docs/en/cli-reference (fetched 2026-03-09).

| v1.1 Feature | CLI Flags Used | Integration Point |
|---------------|----------------|-------------------|
| Interactive: Pause | (protocol-level, not CLI) | `HostCommandProtocol.CreatePauseSession` |
| Interactive: Resume | `--resume <id>`, `--session-id <uuid>` | `ClaudeCodeAdapter.BuildCommandArgs` |
| Interactive: Redirect | `--input-format stream-json` | `ISshHostConnection` stdin pipe |
| Multi-agent: Sub-agents | `--agents '{...}'`, `--agent <name>` | `ClaudeCodeAdapter.BuildCommandArgs` |
| Multi-agent: Budget | `--max-budget-usd`, `--max-turns` | `AgentStartRequest` config |
| Multi-agent: Model routing | `--model sonnet/opus/haiku` | `AgentAdapterConfig` extension |
| Inventory: Version | `claude -v` | SSH command execution |
| Inventory: Tools | `claude agents` | SSH command execution + JSON parse |
| Inventory: Health | `claude auth status` | SSH command execution + JSON parse |
| Worktree: Isolation | `--worktree <name>` / `-w <name>` | `ClaudeCodeAdapter.BuildCommandArgs` |

## What NOT to Add

| Technology | Why Not |
|------------|---------|
| **SignalR** | SSE + SSH stdin already provides bidirectional. SignalR adds WebSocket complexity and a second transport to maintain. |
| **RabbitMQ / Redis** | At 5-10 hosts, SQLite + Channel<T> handles event routing. External queue is operational overhead with no throughput benefit. |
| **gRPC** | The host daemon protocol is JSON-over-SSH-stdin. gRPC would require a daemon process on every host listening on a port. SSH tunneling is simpler and already secured. |
| **Hangfire / Quartz** | Background task scheduling is handled by `BackgroundService` + `Task.Delay` loops. No cron-like scheduling needed. |
| **MediatR** | The codebase uses direct DI injection. At 178 files / ~12K LOC, the indirection of MediatR adds complexity without benefit. |
| **Polly** | SSH reconnection is handled by `SshHostConnection` heartbeat + error detection. Simple retry loops in `BackgroundService` suffice. Consider only if retry patterns proliferate. |
| **Semantic Kernel** | Platform orchestrates CLI tools, not LLM-to-LLM conversations. SK is for when the platform IS an AI agent. |
| **CliWrap** | SSH.NET handles all remote process execution. Local process execution is not needed -- agents run on remote hosts. |
| **ModelContextProtocol SDK** | MCP support is listed as "Future" in PROJECT.md. Do not add until explicitly scoped. |

## Schema Extensions (EF Core Migrations)

No new packages, just new migrations:

```csharp
// SessionEntity additions
public string? ParentSessionId { get; set; }       // FK for sub-agent tracking
// SessionState enum addition
// Paused = 4

// New entity: HostInventoryEntity
public class HostInventoryEntity
{
    public string HostId { get; set; }
    public string AgentType { get; set; }           // "claude-code", "codex", etc.
    public string? CliPath { get; set; }            // "/usr/local/bin/claude"
    public string? Version { get; set; }            // "1.0.34"
    public string? ToolsJson { get; set; }          // JSON array of discovered tools
    public long? DiskFreeMb { get; set; }
    public string? GitVersion { get; set; }         // For worktree support detection
    public DateTimeOffset LastScannedUtc { get; set; }
}

// HostCommandProtocol extensions
public const string PauseSession = "pause-session";
public const string ResumeSession = "resume-session";
public const string InventoryQuery = "inventory-query";
```

## Alternatives Considered

| Category | Recommended | Alternative | Why Not |
|----------|-------------|-------------|---------|
| Bidirectional comms | SSH stdin + SSE | SignalR WebSocket | Second transport, no benefit at scale |
| Event routing | Channel<T> + DurableEventService | Redis Pub/Sub | External dependency, operational overhead |
| Task scheduling | BackgroundService | Hangfire | No cron patterns needed, YAGNI |
| Agent orchestration | Direct ISessionCoordinator calls | Workflow engine (Elsa) | Massive dependency for simple parent-child dispatch |
| Session state | EF Core + SQLite | Redis cache | Already using SQLite, session state is durable not ephemeral |
| Host inventory | SSH polling | Agent on each host | PROJECT.md explicitly says "no automatic tool installation on hosts" |
| Worktree management | Claude Code `--worktree` flag | Manual `git worktree add` via SSH | Claude Code handles all mechanics; let it |
| Placement scoring | Weighted scoring in SimplePlacementEngine | External scheduler (Nomad) | Nomad is future scope; current scale (5-10 hosts) doesn't need it |

## Installation

No new packages to install. v1.1 development starts with the existing `dotnet restore`.

```bash
# Verify existing stack is clean
dotnet build
dotnet test

# Only new work is schema migrations
dotnet ef migrations add AddHostInventory --project src/AgentHub.Orchestration
dotnet ef migrations add AddSessionParentId --project src/AgentHub.Orchestration
```

## Sources

- [Claude Code CLI Reference](https://code.claude.com/docs/en/cli-reference) -- official flag reference (fetched 2026-03-09) -- HIGH confidence
- [Claude Code Sub-agents](https://code.claude.com/docs/en/sub-agents) -- sub-agent definition and coordination (fetched 2026-03-09) -- HIGH confidence
- [Claude Code Agent Teams](https://code.claude.com/docs/en/agent-teams) -- experimental multi-agent teams (fetched 2026-03-09) -- HIGH confidence
- [Claude Code Common Workflows](https://code.claude.com/docs/en/common-workflows) -- worktree workflow documentation -- HIGH confidence
- [System.Threading.Channels - NuGet](https://www.nuget.org/packages/System.Threading.Channels) -- v10.0.3 available, BCL-included in .NET 10 -- HIGH confidence
- Existing codebase analysis (SessionCoordinator, SshBackend, DurableEventService, HostCommandProtocol, ClaudeCodeAdapter, GitWorktreeProvider) -- PRIMARY source -- HIGH confidence
