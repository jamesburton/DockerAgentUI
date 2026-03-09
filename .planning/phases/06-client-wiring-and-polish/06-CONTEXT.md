# Phase 6: Client Wiring and Polish - Context

**Gathered:** 2026-03-09
**Status:** Ready for planning

<domain>
## Phase Boundary

Wire the `POST /api/sessions/{id}/input` endpoint to both CLI and Web clients, populate host resource metrics from SSH status polls, make FleetOverview SSE updates incremental (patch state in-place), and resolve minor tech debt (CS8602 nullable warning, approval endpoint path alignment). The server-side input endpoint already exists — this phase is about client callers and quality improvements.

</domain>

<decisions>
## Implementation Decisions

### Session Input — CLI
- Both modes: positional arg for quick input (`ah session input <id> "text"`), falls back to interactive prompt (Ctrl+D to send) when no text arg provided
- `ah session watch` gets an input hotkey: press 'i' to enter input mode, type message, Enter to send, Esc to cancel — resumes watch after send
- `--json` mode supported on input command for scriptable usage
- Success output: "Input sent to session {id}." (brief confirmation)

### Session Input — Web
- Fixed bottom bar on SessionDetail page: text input + Send button, pinned below terminal output
- Only visible when session is in Running state — hidden for completed/failed sessions
- After sending: input clears with brief "Sent" flash indicator (~1 second)
- Uses existing `SendInputRequest` DTO from Contracts

### Host Metrics Population
- SSH status poll every 30 seconds — coordinator SSHs into each registered host and collects CPU/memory
- Cross-platform support: Windows-first (`Get-Counter` / `systeminfo`), then Linux (`/proc/stat`, `/proc/meminfo`), then macOS (`vm_stat`, `sysctl`)
- OS detection per host — use platform-appropriate commands
- Stale data handling: show last known values with age indicator (e.g., "45% CPU (2m ago)")
- If no data ever collected: show "--"
- Visual indicator (dimmed/gray) when data is older than 2x poll interval (60s)
- Metrics stored in HostRecord's existing CpuPercent/MemUsedMb/MemTotalMb fields

### FleetOverview SSE Patching
- Patch session state in-place on StateChanged events — update matching session's status/duration in the list without full API reload
- Unknown sessionId → single API call to fetch the new session and add to list
- Completed/Failed sessions stay in list with updated status
- Host metrics delivered via SSE on same fleet-wide `/api/events` endpoint using a new `HostMetrics` event kind
- HostMetrics event format: `{kind: HostMetrics, sessionId: null, meta: {hostId, cpu, memUsedMb, memTotalMb}}`
- Web sidebar patches host data from HostMetrics SSE events in real-time (no separate polling needed when SSE enabled)

### Tech Debt
- CS8602 nullable warning in SseStreamReader.cs — fix the nullable reference
- Approval endpoint path alignment (docs vs implementation) — Claude's discretion on which to fix

### Claude's Discretion
- Exact Spectre.Console components for watch input mode (Live context pause/resume approach)
- Interactive prompt implementation for CLI input (Console.ReadLine vs System.CommandLine built-in)
- OS detection mechanism for SSH metric collection
- MudBlazor component choice for the fixed bottom input bar
- "Sent" flash implementation (MudBlazor snackbar vs inline indicator)
- HostMetrics event emission frequency (every poll cycle vs only on change)

</decisions>

<specifics>
## Specific Ideas

- Watch input hotkey should feel like Vim's 'i' key — press 'i' to enter input mode, type freely, Enter sends, Esc cancels
- Fixed bottom bar on web should feel like a chat input — always ready, no friction
- Windows is the most common platform in the fleet, so Windows metric collection should be first priority
- Host metrics age indicator prevents misleading stale data — important for fleet oversight reliability

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- **SendInputRequest** (Contracts/Models.cs): DTO already defined — `SendInputRequest(string Text)`
- **POST /api/sessions/{id}/input** (Program.cs:163-166): Server endpoint fully implemented, calls `coordinator.SendInputAsync`
- **SshBackend.SendInputAsync** (Backends/SshBackend.cs:163): SSH stdin write already implemented
- **HostStatusReport** (HostDaemon/HostDaemonModels.cs:149): Model with CpuPercent, MemoryUsedMb, MemoryTotalMb fields
- **HostRecord** (Contracts/Models.cs:106): Already has nullable CpuPercent, MemUsedMb, MemTotalMb fields
- **SseStreamService** (Web/Services/SseStreamService.cs): Web SSE consumer, used by FleetOverview and SessionDetail
- **DashboardApiClient** (Web/Services/DashboardApiClient.cs): No SendInputAsync yet — needs adding
- **AgentHubApiClient** (Cli/Api/AgentHubApiClient.cs): No SendInputAsync yet — needs adding

### Established Patterns
- CLI commands use Spectre.Console Live display for watch modes (SessionWatchCommand, WatchCommand)
- CLI approval handling exits Live context, shows prompt, restarts Live (Phase 3 decision 03-03)
- Web uses MudBlazor components with dark theme, MudSnackbar for notifications
- Fleet SSE: FleetOverview.razor subscribes to `/api/events` via SseStreamService
- Polling timer (7s) as fallback when SSE disabled
- SessionEventKind enum in Contracts/Models.cs defines event types

### Integration Points
- **AgentHubApiClient**: Add `SendInputAsync(sessionId, text)` method calling POST input endpoint
- **DashboardApiClient**: Add `SendInputAsync(sessionId, text)` method calling POST input endpoint
- **SessionDetail.razor**: Add fixed bottom input bar component
- **SessionWatchCommand.cs**: Add 'i' hotkey handler in Live display loop
- **FleetOverview.razor:109-126**: Replace `LoadDataAndRefresh()` calls with in-place session patching
- **DurableEventService**: Add HostMetrics event emission from metric poll service
- **SessionEventKind**: Add `HostMetrics` enum value

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 06-client-wiring-and-polish*
*Context gathered: 2026-03-09*
