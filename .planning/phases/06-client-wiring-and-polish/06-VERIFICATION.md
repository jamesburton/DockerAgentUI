---
phase: 06-client-wiring-and-polish
verified: 2026-03-09T16:00:00Z
status: passed
score: 13/13 must-haves verified
---

# Phase 6: Client Wiring and Polish Verification Report

**Phase Goal:** All API endpoints have client callers, host resource metrics are populated, and minor quality issues are resolved
**Verified:** 2026-03-09T16:00:00Z
**Status:** passed
**Re-verification:** No -- initial verification

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | CLI user can send input to a running session via 'ah session input id text' | VERIFIED | `Program.cs` lines 174-209: `input` command registered with `id` and `text` args, calls `SendInputAsync`, handles errors |
| 2 | CLI user can enter input mode during session watch by pressing 'i' | VERIFIED | `SessionWatchCommand.cs` lines 98-103: `Console.KeyAvailable` check for 'i' key, lines 147-158: input prompt with `SendInputAsync` call |
| 3 | Web user can type and send input from a fixed bottom bar on SessionDetail page | VERIFIED | `SessionDetail.razor` lines 75-93: sticky bottom bar with MudTextField and Send button; lines 203-221: `SendInput` method calls `Api.SendInputAsync` |
| 4 | Input bar is only visible when session state is Running | VERIFIED | `SessionDetail.razor` line 75: `@if (_session?.State == SessionState.Running)` gates the input bar |
| 5 | SendInputAsync routes through existing SessionCoordinator sanitization pipeline | VERIFIED | Both clients POST to `/api/sessions/{id}/input` which maps to `SessionCoordinator.SendInputAsync` in Program.cs |
| 6 | Host resource metrics (CPU, memory) are populated from SSH status polls every 30 seconds | VERIFIED | `HostMetricPollingService.cs`: 197-line BackgroundService with `_pollInterval = TimeSpan.FromSeconds(30)`, SSH command execution, DB persistence |
| 7 | HostMetrics SSE events are emitted on /api/events for real-time delivery to Web dashboard | VERIFIED | `HostMetricPollingService.cs` lines 128-139: `_events.EmitAsync` with `SessionEventKind.HostMetrics` and meta dictionary |
| 8 | OS-appropriate commands collect metrics from Windows, Linux, and macOS hosts | VERIFIED | `HostMetricPollingService.GetMetricCommand` lines 151-170: distinct commands for Windows (PowerShell), Linux (proc/free), macOS (top/sysctl/vm_stat) |
| 9 | HostRecord CpuPercent/MemUsedMb/MemTotalMb fields contain real values after first poll | VERIFIED | `HostEntity.cs` lines 15-18: metric properties; `EntityMappers.cs` lines 96-98: ToDto passes CpuPercent, MemUsedMb, MemTotalMb to HostRecord |
| 10 | FleetOverview patches session state in-place on StateChanged SSE events without full API reload | VERIFIED | `FleetOverview.razor` lines 109-133: in-place `_sessions[idx] = _sessions[idx] with { State = newState }` patching, no `LoadDataAndRefresh` call |
| 11 | Unknown session IDs trigger a single API fetch to add the new session to the list | VERIFIED | `FleetOverview.razor` lines 129-131: `GetSessionAsync(evt.SessionId)` for unknown sessions, adds to `_sessions` |
| 12 | HostSidebar updates CPU/memory bars from HostMetrics SSE events in real-time | VERIFIED | `FleetOverview.razor` lines 135-141: `OnHostMetricsReceived` dispatches to `_hosts` list; `HostSidebar.razor` lines 49-92: renders CPU/MEM bars from HostRecord with stale "--" indicator |
| 13 | CS8602 nullable warning in SseStreamReader.cs is resolved | VERIFIED | `SseStreamReader.cs` line 83: `sseItem.EventId.Length > 0 ? sseItem.EventId.ToString()! : ""` -- defensive check replaces warning-prone code |

**Score:** 13/13 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `src/AgentHub.Cli/Api/AgentHubApiClient.cs` | SendInputAsync method | VERIFIED | Lines 65-71: `SendInputAsync` POSTs to `/api/sessions/{id}/input` with `SendInputRequest` |
| `src/AgentHub.Cli/Program.cs` | session input command registration | VERIFIED | Lines 174-209: `input` command with id/text args, stdin fallback, error handling |
| `src/AgentHub.Cli/Commands/Session/SessionWatchCommand.cs` | Input hotkey handler in watch mode | VERIFIED | Lines 98-103: KeyAvailable check; lines 147-158: input prompt and SendInputAsync |
| `src/AgentHub.Web/Services/DashboardApiClient.cs` | SendInputAsync method | VERIFIED | Lines 68-74: `SendInputAsync` POSTs to `/api/sessions/{id}/input` with `SendInputRequest` |
| `src/AgentHub.Web/Components/Pages/SessionDetail.razor` | Fixed bottom input bar for running sessions | VERIFIED | Lines 75-93: sticky MudPaper with MudTextField and Send button; lines 203-227: SendInput + OnInputKeyDown handlers |
| `src/AgentHub.Orchestration/Monitoring/HostMetricPollingService.cs` | BackgroundService for host metric polling | VERIFIED | 197 lines, SSHs into hosts, parses metrics, persists to DB, emits SSE events |
| `src/AgentHub.Orchestration/Data/Entities/HostEntity.cs` | CpuPercent, MemUsedMb, MemTotalMb, MetricsUpdatedUtc columns | VERIFIED | Lines 15-18: all four properties present |
| `src/AgentHub.Contracts/Models.cs` | HostMetrics value in SessionEventKind enum | VERIFIED | Line 87: `HostMetrics` enum value present |
| `src/AgentHub.Orchestration/Data/EntityMappers.cs` | Metric fields mapped in HostRecord ToDto | VERIFIED | Lines 96-98: CpuPercent, MemUsedMb, MemTotalMb passed to HostRecord constructor |
| `src/AgentHub.Web/Components/Pages/FleetOverview.razor` | Incremental SSE state patching | VERIFIED | Lines 109-153: StateChanged, HostMetrics, SessionCompleted handled with in-place patching |
| `src/AgentHub.Web/Components/Shared/HostSidebar.razor` | Real-time host metric display | VERIFIED | Lines 49-92: CPU/MEM progress bars with stale "--" indicator when null |
| `src/AgentHub.Cli/Api/SseStreamReader.cs` | CS8602 nullable warning fix | VERIFIED | Line 83: defensive length check with null-forgiving operator |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| Program.cs (session input cmd) | AgentHubApiClient.SendInputAsync | Command handler | WIRED | Line 197: `await client.SendInputAsync(sessionId, text!, ct)` |
| SessionWatchCommand.cs | AgentHubApiClient.SendInputAsync | 'i' hotkey input mode | WIRED | Line 153: `await apiClient.SendInputAsync(sessionId, text, ct)` |
| SessionDetail.razor | DashboardApiClient.SendInputAsync | Send button click | WIRED | Line 209: `await Api.SendInputAsync(SessionId!, _inputText)` |
| HostMetricPollingService | ISshHostConnectionFactory | SSH command execution | WIRED | Line 103: `_connectionFactory.Create(...)` then `conn.ExecuteCommandAsync(command, ct)` |
| HostMetricPollingService | DurableEventService | Emits HostMetrics event | WIRED | Line 128: `await _events.EmitAsync(new SessionEvent(...HostMetrics...))` |
| EntityMappers.ToDto (Host) | HostEntity.CpuPercent | Maps entity to HostRecord | WIRED | Lines 96-98: `CpuPercent: entity.CpuPercent, MemUsedMb: entity.MemUsedMb, MemTotalMb: entity.MemTotalMb` |
| FleetOverview.razor | DashboardApiClient.GetSessionAsync | Single fetch for unknown sessions | WIRED | Line 130: `var newSession = await Api.GetSessionAsync(evt.SessionId)` |
| FleetOverview.razor | HostSidebar via _hosts parameter | HostMetrics SSE delivery | WIRED | Lines 168-179: `OnHostMetricsReceived` updates `_hosts[idx]` with `with` expression; line 14: `Hosts="_hosts"` passed to HostSidebar |
| HostMetricPollingService | Service DI registration | AddHostedService | WIRED | Program.cs line 62: `builder.Services.AddHostedService<HostMetricPollingService>()` |

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|------------|-------------|--------|----------|
| AGENT-03 | 06-01-PLAN | Inputs to agents pass through configurable sanitization layer | SATISFIED | CLI and Web clients POST to `/api/sessions/{id}/input`, which routes through `SessionCoordinator.SendInputAsync` (sanitization pipeline implemented in Phase 2). Client-side callers now complete the end-to-end path. |
| AGENT-04 | 06-01-PLAN | Destructive agent actions trigger approval/elevation flow | SATISFIED | SendInputAsync routes through SessionCoordinator which handles approval flow (Phase 2). CLI watch mode already supports approval prompts. Client-side input callers now exercise this path end-to-end. |
| MON-02 | 06-02-PLAN, 06-03-PLAN | User can view resource usage (CPU, memory) per host | SATISFIED | HostMetricPollingService collects CPU/memory via SSH every 30s. HostEntity stores metrics. EntityMappers passes to HostRecord DTO. HostSidebar displays CPU/MEM bars. FleetOverview dispatches HostMetrics SSE events for real-time updates. CLI `host status` command displays metrics. |

No orphaned requirements found -- all three requirement IDs mapped to this phase in REQUIREMENTS.md are accounted for in the plans.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| None | -- | -- | -- | No anti-patterns detected |

No TODO/FIXME/PLACEHOLDER/HACK comments found in any modified files. No empty implementations, no stub returns. The "Placeholder" string in SessionDetail.razor line 80 is a UI field placeholder attribute, not a code stub.

### Human Verification Required

### 1. Web Input Bar Interaction

**Test:** Navigate to a running session's detail page. Type text in the input bar and click Send (or press Enter).
**Expected:** Snackbar shows "Sent", input field clears, session receives the input.
**Why human:** Requires running application with an active session to verify end-to-end data flow through SSE and SessionCoordinator.

### 2. CLI Watch Mode Input Hotkey

**Test:** Run `ah session watch <id>` on a running session. Press 'i' to enter input mode, type text, press Enter.
**Expected:** Live display pauses, "Input>" prompt appears, text is sent, "Input sent." confirmation shown, live display resumes.
**Why human:** Requires interactive terminal with running session to verify Console.KeyAvailable behavior.

### 3. Host Metric Real-Time Updates

**Test:** Enable "Live Updates" on FleetOverview. Wait for host metric polling cycle (30s).
**Expected:** HostSidebar CPU/MEM bars update in real-time without page reload.
**Why human:** Requires running hosts with SSH access configured to verify actual metric collection and SSE delivery.

### 4. Stale Metric Indicator

**Test:** View HostSidebar for a host that has never been polled.
**Expected:** CPU and MEM columns show "--" instead of empty or 0%.
**Why human:** Requires visual inspection of HostSidebar rendering.

### Gaps Summary

No gaps found. All 13 observable truths are verified. All artifacts exist, are substantive (not stubs), and are properly wired. All three requirement IDs (AGENT-03, AGENT-04, MON-02) are satisfied. No anti-patterns detected in modified files.

---

_Verified: 2026-03-09T16:00:00Z_
_Verifier: Claude (gsd-verifier)_
