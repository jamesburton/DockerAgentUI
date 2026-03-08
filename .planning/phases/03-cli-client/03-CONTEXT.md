# Phase 3: CLI Client - Context

**Gathered:** 2026-03-08
**Status:** Ready for planning

<domain>
## Phase Boundary

A command-line tool (`ah`) serves as the primary interface for all platform operations — launching, monitoring, and managing agent sessions across the fleet. Supports both interactive (rich TUI) and scriptable (JSON output, exit codes) modes. Covers session CRUD, host status with resource monitoring, config inspection, live session watching, and approval handling. Web dashboard and MAUI client are separate phases.

</domain>

<decisions>
## Implementation Decisions

### Command Structure & Naming
- Tool name: `ah` (short, fast to type, like `gh` for GitHub)
- Primary structure: noun-verb groups (`ah session start`, `ah host list`, `ah config show`)
- Top-level shortcuts for frequent commands: `ah run` = `ah session start`, `ah ls` = `ah session list`
- Three resource groups: `session`, `host`, `config` — approvals handled inline during session watch, not a separate group
- Launch syntax: positional agent type + optional `--host` flag. `ah run claude "fix the login bug" --host dev-box`
- Prompt as second positional arg or `--prompt/-p` flag
- Auto-placement when `--host` omitted (existing SimplePlacementEngine)

### Output Formatting & Verbosity
- Default output: compact columnar tables (like `docker ps`, `kubectl get`)
- Scriptable mode: `--json` flag on any command for machine-readable JSON output — no colors, no spinners
- Exit codes: 0=success, 1=error, 2=timeout
- Rich TUI elements via Spectre.Console: colored tables, spinners, progress bars, emoji status indicators
- Colors on by default, `--no-color` flag and NO_COLOR env var to disable
- Three verbosity levels: default (essential info), `-v` (detail: timing, IDs), `-q` (errors only)

### Live Monitoring Experience
- `ah session watch <id>`: Live dashboard panel — Spectre.Console Live display with status header (session state, duration, host) and scrolling log below
- Events color-coded by type (stdout=default, stderr=red, state=yellow)
- `ah run` auto-attaches to output by default; `--detach/-d` flag for background launch
- Ctrl+C detaches without stopping session (session keeps running)
- `ah session logs <id>`: Last 100 lines by default, `--all` for full history, `--tail N` to customize, `--follow/-f` to attach if still running
- Single-session watch for individual detail
- Fleet overview: `ah watch` (no ID) shows live table of all running sessions with status updates
- Enter and leave sessions without terminating them
- Split-pane multi-session watch deferred to release candidate level (noted for future)

### Notification & Approval UX
- Approval requests during session watch: auto-popup Spectre.Console panel overlaying watch output, showing action details, risk level, accept/reject options. Returns to log after resolution
- Approval queue modes — toggleable with keypress + visual indicator of current mode:
  - FIFO (one at a time, oldest first)
  - Pick-one (numbered list, user selects)
  - Per-session (only show approvals for currently-watched session)
- Background notifications: terminal bell when events arrive + pending notification summary shown on next `ah` command ("Session abc123 completed 2m ago")
- Background listener: `ah listen` runs in dedicated terminal, prints notification stream for session completions, errors, approval requests
- Resource monitoring: `ah host status --watch` uses Spectre.Console Live to refresh CPU/memory metrics per host every few seconds

### Claude's Discretion
- System.CommandLine vs Spectre.Console.Cli for command parsing (both viable in .NET)
- Exact Spectre.Console component choices (Table, Live, Status, Panel, etc.)
- HTTP client implementation for API communication (reuse patterns from MAUI ApiClient or fresh)
- SSE client implementation for event streaming
- Config file location and format for CLI settings (server URL, default preferences)
- Exact color palette and status indicator symbols

</decisions>

<specifics>
## Specific Ideas

- All-flags and profile-based launch modes noted as backlog items for later versions
- OS-level notifications (toast/notification center) planned for when web/app client arrives — keep notification abstraction extensible
- Split-pane multi-session watch is the target for release candidate — design the session watch abstraction to support it later
- `ah listen` daemon is a v1 feature, not deferred — should work from day one for operators monitoring a fleet from a terminal

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- **AgentHub.Contracts/Models.cs**: All DTOs (SessionSummary, StartSessionRequest, SendInputRequest, SessionEventKind) ready for CLI consumption
- **AgentHub.Maui/ApiClient.cs**: Existing HTTP client pattern (GetSessionsAsync, GetHostsAsync, GetSkillsAsync) — shows the API contract shape
- **System.CommandLine**: Already in build output (Aspire dependency) — available for command parsing
- **CliWrap**: Already referenced — could be useful for subprocess management if needed

### Established Patterns
- REST API endpoints fully built: sessions CRUD, hosts, skills, policy, approvals, events
- SSE endpoints: per-session (`/api/sessions/{id}/events`) and fleet-wide (`/api/events`)
- Approval resolution: `POST /api/approvals/{id}/resolve` with ApprovalResolveRequest
- Session history: `GET /api/sessions/{id}/history` with pagination (page, pageSize, kind filter)
- Force-kill: `DELETE /api/sessions/{id}?force=true`
- JSON serialization: JsonSerializerDefaults.Web (camelCase) throughout

### Integration Points
- CLI connects to AgentHub.Service via HTTP REST + SSE
- Server URL configuration needed (default localhost for dev, configurable for deployment)
- All session operations go through ISessionCoordinator on server side
- Events streamed via DurableEventService + SseSubscriptionManager

</code_context>

<deferred>
## Deferred Ideas

- All-flags launch mode (`--agent claude --host dev-box --prompt "..."`) — backlog for scripting ergonomics
- Profile-based launch (`ah run my-task-profile`) — backlog for reusable task definitions
- Split-pane multi-session watch — release candidate level, not v1
- OS-level toast notifications — implement with web/app client (Phase 4+)
- `ah approval` as separate resource group — if approval workflows get complex enough to warrant it

</deferred>

---

*Phase: 03-cli-client*
*Context gathered: 2026-03-08*
