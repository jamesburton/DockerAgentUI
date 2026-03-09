# AgentSafeEnv

## What This Is

A multi-agent orchestration platform that deploys, monitors, and controls AI coding agents across a fleet of machines. Provides a coordinator REST API, real-time SSE event streaming, CLI client, and Blazor web dashboard for launching sessions, streaming output, managing approvals, and monitoring host resources — all from one place.

## Core Value

See what every agent is doing across every machine in real-time, and deploy new agent sessions to any registered host from one place.

## Requirements

### Validated

- ✓ Launch agent sessions on remote machines via SSH backend — v1.0
- ✓ Monitor running agent sessions across all machines (output, status) — v1.0
- ✓ Stream agent events in real-time via SSE — v1.0
- ✓ Support multiple agent types via adapter pattern (Claude Code first) — v1.0
- ✓ Register and track host machines with DB-backed registry — v1.0
- ✓ Config-driven skill and policy definitions (JSON/YAML) — v1.0
- ✓ Session lifecycle management (start, monitor, stop, force-kill) — v1.0
- ✓ Fire-and-forget task sessions — v1.0
- ✓ Approval/elevation flow for destructive agent actions — v1.0
- ✓ Resource usage visibility (CPU, memory via SSH polling) — v1.0
- ✓ Input sanitization layer with trust tiers — v1.0
- ✓ Coordinator API with session CRUD, host listing, event streaming — v1.0
- ✓ SSH execution backend with two-phase stop — v1.0
- ✓ CLI client with live watch, notifications, inline approvals — v1.0
- ✓ Blazor web dashboard with fleet overview and live terminal — v1.0
- ✓ EF Core SQLite persistence with pluggable provider — v1.0
- ✓ Session history with typed DTOs, pagination, kind filtering — v1.0
- ✓ Configurable permission-skip flags per agent — v1.0
- ✓ Incremental SSE patching for fleet overview — v1.0
- ✓ Bidirectional session input (CLI + Web) — v1.0

### Active

- [ ] Interactive sessions with bidirectional steering mid-run (pause, resume, redirect)
- [ ] Multi-agent coordination across machines (dispatch, sub-agent spawning, resource-aware)
- [ ] Host inventory with agent tool discovery, versions, and health checks
- [ ] Git worktree-based isolation per agent session (`--worktree` flag support)

## Current Milestone: v1.1 Multi-Agent & Interactive

**Goal:** Enable interactive agent steering, multi-agent coordination across machines, host inventory discovery, and git worktree isolation for parallel sessions.

**Target features:**
- Interactive sessions: send follow-up instructions, pause/resume, change direction mid-run
- Multi-agent coordination: dispatch tasks to different machines, spawn sub-agents when resources busy
- Host inventory: discover installed agent CLIs, versions, disk space, tools
- Git worktree isolation: `--worktree` support so co-hosted agents work on the same repo safely

### Future

- Blob-backed artifact and snapshot storage
- Container-based execution environments
- Nomad execution backend for scheduler-based placement
- MCP protocol support as agent control protocol
- Token spend tracking per session
- Diff review workflow (view agent changes, approve/reject)
- MAUI desktop/mobile client

### Out of Scope

- Multi-tenant / multi-user auth — single operator, adds RBAC/isolation/billing complexity
- Agent-to-agent direct communication — debugging nightmares, use orchestrator-mediated coordination
- Auto-merge of agent PRs — human-in-the-loop for merges
- Natural language task decomposition — operator decomposes, system executes
- Automatic tool installation on hosts — fragile and security concern
- Mobile-first UI — desktop-first, mobile-responsive as bonus
- OpenClaw / NanoClaw agent support — insufficient information yet

## Context

Shipped v1.0 with 11,989 LOC C# across 178 files. Tech stack: .NET 10, ASP.NET Core, Aspire, EF Core SQLite, SSH.NET, MudBlazor, System.CommandLine. Built in 2 days (122 commits). All 20 v1 requirements validated, audit passed with tech debt cleaned.

The platform wraps existing agent CLIs (e.g., `claude`, `codex`) via SSH and orchestrates them — agents run unmodified on target machines.

## Constraints

- **Stack**: .NET 10 / ASP.NET Core / Aspire — non-negotiable
- **Platform**: Windows-first, cross-platform where .NET supports it
- **Agents**: Must work with agents as-they-are (CLI tools), no agent modifications
- **Data**: EF Core with provider abstraction — SQLite now, swap later
- **Safety**: Trust tier policy, approval gating, sanitization — harden incrementally

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| .NET 10 / ASP.NET Core stack | User's primary stack, existing scaffold | ✓ Good |
| CLI-first UI, then Web (Blazor) | Fastest path to useful tool | ✓ Good — both shipped in v1.0 |
| SSH as execution backend | Direct control, simpler than Nomad for v1 | ✓ Good — works for 5-10 hosts |
| Incremental sandboxing (policy → approval → trust tiers) | Get working fast, harden over time | ✓ Good |
| EF Core SQLite | Simplest start, pluggable provider | ✓ Good — may need Postgres at scale |
| Dual DbContext registration (Pool + Factory) | Scoped DI + singleton services | ✓ Good |
| DurableEventService singleton with IDbContextFactory | Event persistence + live SSE | ✓ Good |
| Channel\<T\> bridge for SseStreamReader | Avoids yield-in-try-catch C# restriction | ✓ Good |
| In-memory sorting for SQLite DateTimeOffset queries | SQLite limitation workaround | ⚠️ Revisit at scale |
| Host daemon protocol via single-line JSON over SSH | SSH stdin/stdout compatibility | ✓ Good |
| Aspire SDK 9.2.1 (workload deprecated in .NET 10) | Forced upgrade from preview | ✓ Good |
| Extern alias for test project Program ambiguity | Multi-project test isolation | ✓ Good |

---
*Last updated: 2026-03-09 after v1.1 milestone started*
