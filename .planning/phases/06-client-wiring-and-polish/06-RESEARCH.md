# Phase 6: Client Wiring and Polish - Research

**Researched:** 2026-03-09
**Domain:** Client API integration, host metrics collection, SSE incremental updates, tech debt
**Confidence:** HIGH

## Summary

This phase wires existing server-side endpoints to CLI and Web clients, populates host resource metrics from SSH polling, converts FleetOverview SSE handling from full-reload to incremental patching, and resolves minor tech debt. The server-side `POST /api/sessions/{id}/input` endpoint is fully implemented through `SessionCoordinator.SendInputAsync` -> `SshBackend.SendInputAsync`. Both `AgentHubApiClient` (CLI) and `DashboardApiClient` (Web) need a `SendInputAsync` method added following their established patterns. The CLI needs a new `session input` command, and the `session watch` command needs an 'i' hotkey for input mode. The Web SessionDetail page needs a fixed bottom input bar.

For host metrics, a critical gap exists: `HostEntity` lacks `CpuPercent`, `MemUsedMb`, and `MemTotalMb` columns, and `HostStatusReport` lacks a `MemoryTotalMb` field. A new `HostMetricPollingService` (BackgroundService) must SSH into each host on a 30-second interval, collect OS-appropriate metrics, and update HostEntity fields. The `SessionEventKind` enum needs a `HostMetrics` value added, and DurableEventService must emit HostMetrics events for SSE delivery to the Web dashboard.

FleetOverview currently calls `LoadDataAndRefresh()` (full API reload) on every `StateChanged` SSE event. This must be converted to in-place state patching: update the matching session's status in the `_sessions` list directly, and only fetch from API for unknown session IDs.

**Primary recommendation:** Split into 3 plans: (1) Client input wiring (CLI + Web), (2) Host metrics collection + SSE delivery, (3) FleetOverview incremental patching + tech debt fixes.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- CLI session input: positional arg (`ah session input <id> "text"`) + interactive prompt fallback (Ctrl+D to send) + `--json` mode
- CLI session watch: 'i' hotkey enters input mode, Enter sends, Esc cancels, resumes watch after send
- Web input: fixed bottom bar on SessionDetail page, only visible when Running, "Sent" flash after send
- Uses existing `SendInputRequest` DTO from Contracts
- SSH status poll every 30 seconds for host metrics
- Cross-platform: Windows-first (Get-Counter/systeminfo), then Linux (/proc/stat, /proc/meminfo), then macOS (vm_stat, sysctl)
- OS detection per host for platform-appropriate commands
- Stale data: show last known values with age indicator, "--" when never collected, dimmed when > 60s old
- Metrics stored in HostRecord's CpuPercent/MemUsedMb/MemTotalMb fields
- FleetOverview SSE: patch session state in-place on StateChanged, single API call for unknown sessionId, completed/failed stay in list
- HostMetrics SSE: new event kind on `/api/events`, format `{kind: HostMetrics, sessionId: null, meta: {hostId, cpu, memUsedMb, memTotalMb}}`
- Web sidebar patches host data from HostMetrics SSE events in real-time
- CS8602 nullable warning in SseStreamReader.cs -- fix nullable reference
- Approval endpoint path alignment -- Claude's discretion on which to fix

### Claude's Discretion
- Exact Spectre.Console components for watch input mode (Live context pause/resume approach)
- Interactive prompt implementation for CLI input (Console.ReadLine vs System.CommandLine built-in)
- OS detection mechanism for SSH metric collection
- MudBlazor component choice for the fixed bottom input bar
- "Sent" flash implementation (MudBlazor snackbar vs inline indicator)
- HostMetrics event emission frequency (every poll cycle vs only on change)

### Deferred Ideas (OUT OF SCOPE)
None -- discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| AGENT-03 | Inputs to agents pass through a configurable sanitization layer | SendInputAsync already routes through SessionCoordinator which calls ISanitizationService.Evaluate -- client input wiring enables end-to-end path |
| AGENT-04 | Destructive agent actions trigger an approval/elevation flow requiring human confirmation | SendInputAsync already evaluates trust tiers via EvaluateWithTrustTier -- client input wiring completes the flow with user-facing input UI |
| MON-02 | User can view resource usage (CPU, memory) per host | HostSidebar already renders CPU/memory bars when data is present -- need HostMetricPollingService to populate data and SSE delivery |
</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| System.CommandLine | 2.0.0-beta4 | CLI command definitions | Already used for all CLI commands (session start/stop/watch/logs) |
| Spectre.Console | 0.49+ | Terminal UI (Live display, prompts) | Already used for watch mode, approval prompts, tables |
| MudBlazor | 8.x | Web UI components | Already used throughout Web dashboard (cards, buttons, snackbars) |
| SSH.NET | via ISshHostConnection | SSH command execution | Already used for SshBackend, ISshHostConnectionFactory pattern established |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| System.Threading.Channels | built-in | SSE event bridging | Already used in SseStreamReader and DurableEventService |
| System.Net.ServerSentEvents | built-in (.NET 10) | SSE parsing | Already used in SseStreamReader and SseStreamService |
| System.Text.Json | built-in | JSON serialization | Already used everywhere for API communication |

### Alternatives Considered
None -- all libraries are already established in the project. No new dependencies needed.

## Architecture Patterns

### Recommended Approach for Each Work Area

#### 1. Client API Method Pattern (SendInputAsync)
Follow exact pattern of existing methods in both API clients:

```csharp
// AgentHubApiClient (CLI) -- follows PostAsJsonAsync + EnsureSuccessStatusCode pattern
public async Task SendInputAsync(string sessionId, string text, CancellationToken ct = default)
{
    var response = await _http.PostAsJsonAsync(
        $"/api/sessions/{Uri.EscapeDataString(sessionId)}/input",
        new SendInputRequest(text), s_json, ct);
    response.EnsureSuccessStatusCode();
}

// DashboardApiClient (Web) -- same pattern
public async Task SendInputAsync(string sessionId, string text, CancellationToken ct = default)
{
    var response = await http.PostAsJsonAsync(
        $"/api/sessions/{Uri.EscapeDataString(sessionId)}/input",
        new SendInputRequest(text), s_json, ct);
    response.EnsureSuccessStatusCode();
}
```

#### 2. CLI Command Pattern (session input)
Follow existing command registration pattern in Program.cs (lines 46-206):

```csharp
// New command: ah session input <id> [text]
var cmd = new Command("input", "Send input to a running session");
var idArg = new Argument<string>("id", "Session ID");
var textArg = new Argument<string?>("text", () => null, "Input text (omit for interactive)");
cmd.AddArgument(idArg);
cmd.AddArgument(textArg);
cmd.AddOption(jsonOption);
cmd.SetHandler(async (pr) => { ... });
sessionCommand.Add(cmd);
```

#### 3. Watch Command Input Mode Pattern
Follow existing approval handling pattern -- exit Live context, handle input, restart Live:

```csharp
// In RunLiveModeAsync, similar to pendingApproval handling:
if (Console.KeyAvailable && Console.ReadKey(true).KeyChar == 'i')
{
    inputRequested = true;
    return; // Exit Live context
}

// After Live block (same as approval pattern):
if (inputRequested)
{
    AnsiConsole.Markup("[yellow]Input> [/]");
    var text = Console.ReadLine();
    if (!string.IsNullOrEmpty(text))
    {
        await apiClient.SendInputAsync(sessionId, text, ct);
        AnsiConsole.MarkupLine("[green]Input sent.[/]");
    }
    continue; // Restart Live display
}
```

#### 4. HostMetricPollingService Pattern
Follow `SessionMonitorService` pattern (BackgroundService with IDbContextFactory):

```csharp
public sealed class HostMetricPollingService : BackgroundService
{
    private readonly IDbContextFactory<AgentHubDbContext> _dbFactory;
    private readonly ISshHostConnectionFactory _connectionFactory;
    private readonly DurableEventService _events;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await PollAllHostsAsync(stoppingToken);
            await Task.Delay(_pollInterval, stoppingToken);
        }
    }
}
```

#### 5. FleetOverview Incremental Patch Pattern
Replace `LoadDataAndRefresh()` with in-place session state update:

```csharp
// In SSE handler, instead of: _ = LoadDataAndRefresh();
if (evt.Kind == SessionEventKind.StateChanged)
{
    var idx = _sessions.FindIndex(s => s.SessionId == evt.SessionId);
    if (idx >= 0)
    {
        // Parse new state from event data/meta and create updated SessionSummary
        if (evt.Meta?.TryGetValue("newState", out var stateStr) == true
            && Enum.TryParse<SessionState>(stateStr, out var newState))
        {
            _sessions[idx] = _sessions[idx] with { State = newState };
        }
    }
    else
    {
        // Unknown session -- fetch just this one
        var newSession = await Api.GetSessionAsync(evt.SessionId);
        if (newSession is not null) _sessions.Add(newSession);
    }
}
```

### Anti-Patterns to Avoid
- **Full API reload on every SSE event:** This is the current FleetOverview behavior and the explicit thing to fix
- **Blocking Console.ReadKey in Live context:** Spectre.Console Live display cannot handle interactive input -- must exit Live first
- **Hardcoded OS commands without detection:** Each host has an `Os` field -- use it to select the right metric commands
- **Storing metrics only in-memory:** Metrics must survive service restart -- persist to HostEntity DB fields

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| SSE event delivery | Custom WebSocket/polling | Existing DurableEventService + SseSubscriptionManager | Already handles broadcast, replay, Last-Event-ID |
| SSH command execution | Raw SSH library calls | ISshHostConnectionFactory + ISshHostConnection | Already abstracted for DI, testability, connection pooling |
| Terminal live display | Console.Clear() loops | Spectre.Console AnsiConsole.Live() | Already used in watch command, handles resize, color codes |
| Input sanitization | Custom filtering | SessionCoordinator.SendInputAsync | Already wired with ISanitizationService.Evaluate and trust tier checks |

## Common Pitfalls

### Pitfall 1: Spectre.Console Live Context Input Blocking
**What goes wrong:** Attempting to read Console.ReadKey() inside a Live display callback deadlocks or produces garbled output.
**Why it happens:** Spectre.Console Live takes exclusive control of stdout rendering. Console input operations conflict.
**How to avoid:** Follow the established approval pattern -- exit Live context, handle interactive input, then restart Live with buffered lines.
**Warning signs:** Console output becomes garbled, keystrokes don't register, or display freezes.

### Pitfall 2: HostEntity Missing Metric Columns
**What goes wrong:** HostEntity has no CpuPercent, MemUsedMb, MemTotalMb properties. Attempting to store metrics will fail.
**Why it happens:** Original entity was designed for registration only, not dynamic metrics.
**How to avoid:** Add `CpuPercent` (double?), `MemUsedMb` (long?), `MemTotalMb` (long?), `MetricsUpdatedUtc` (DateTimeOffset?) to HostEntity. Update EntityMapper.ToDto() to populate HostRecord fields. Generate EF migration.
**Warning signs:** Build errors when trying to set entity properties.

### Pitfall 3: HostStatusReport Missing MemoryTotalMb
**What goes wrong:** The HostStatusReport model has MemoryUsedMb but NOT MemoryTotalMb. Cannot calculate memory percentage without total.
**Why it happens:** Original model was designed for daemon reporting which only reported used memory.
**How to avoid:** Add `MemoryTotalMb` property to HostStatusReport. Update SSH metric collection commands to gather total memory.
**Warning signs:** Memory bars always show "--" because MemTotalMb is null.

### Pitfall 4: SSE SessionEvent Reuse for HostMetrics
**What goes wrong:** SessionEvent requires a SessionId, but HostMetrics events are host-level, not session-level.
**Why it happens:** The SessionEvent model was designed for session-specific events.
**How to avoid:** Use SessionId = "" or a sentinel value for host-level events. Store host metadata in the Meta dictionary. The CONTEXT.md specifies `sessionId: null` -- use empty string since SessionEvent.SessionId is non-nullable string.
**Warning signs:** Null reference exceptions if trying to set SessionId to null on a non-nullable field.

### Pitfall 5: Blazor StateHasChanged from Background Thread
**What goes wrong:** Calling StateHasChanged from SSE background task without InvokeAsync causes cross-thread exception.
**Why it happens:** Blazor Server components must update on the synchronization context.
**How to avoid:** Always wrap with `await InvokeAsync(StateHasChanged)` -- already used in FleetOverview.razor and SessionDetail.razor.
**Warning signs:** InvalidOperationException about synchronization context.

### Pitfall 6: CS8602 on SseStreamReader.cs Line 83
**What goes wrong:** `sseItem.EventId.ToString()` where EventId is `ReadOnlyMemory<char>` and may be empty/default.
**Why it happens:** SseParser returns EventId as ReadOnlyMemory<char> which always has a ToString() but analyzer flags it.
**How to avoid:** The current code (line 83: `var eventId = sseItem.EventId.ToString()`) is actually safe since ReadOnlyMemory<char>.ToString() never returns null. The CS8602 warning is likely about the `sseItem` itself potentially being null in the nullable analysis flow. Fix by adding a null check or suppression.
**Warning signs:** CS8602 build warning.

## Code Examples

### Adding SendInputAsync to CLI API Client
```csharp
// In AgentHubApiClient.cs, add after StopSessionAsync:
public async Task SendInputAsync(string sessionId, string text, CancellationToken ct = default)
{
    var response = await _http.PostAsJsonAsync(
        $"/api/sessions/{Uri.EscapeDataString(sessionId)}/input",
        new SendInputRequest(text), s_json, ct);
    response.EnsureSuccessStatusCode();
}
```

### Web Fixed Bottom Input Bar (MudBlazor)
```razor
@* Add inside SessionDetail.razor, after TerminalOutput, inside the else block *@
@if (_session.State == SessionState.Running)
{
    <MudPaper Class="pa-2" Style="position: sticky; bottom: 0; z-index: 10;"
              Elevation="4">
        <MudStack Row AlignItems="AlignItems.Center">
            <MudTextField @bind-Value="_inputText"
                          Placeholder="Send input to session..."
                          Variant="Variant.Outlined"
                          Immediate="true"
                          OnKeyDown="OnInputKeyDown"
                          Disabled="_sending"
                          Class="flex-grow-1" />
            <MudButton Variant="Variant.Filled" Color="Color.Primary"
                       OnClick="SendInput" Disabled="@(string.IsNullOrWhiteSpace(_inputText) || _sending)">
                Send
            </MudButton>
        </MudStack>
    </MudPaper>
}
```

### HostEntity Metric Fields Addition
```csharp
// Add to HostEntity.cs:
public double? CpuPercent { get; set; }
public long? MemUsedMb { get; set; }
public long? MemTotalMb { get; set; }
public DateTimeOffset? MetricsUpdatedUtc { get; set; }
```

### EntityMapper Update for Metrics
```csharp
// Update ToDto in EntityMappers.cs:
return new HostRecord(
    HostId: entity.HostId,
    DisplayName: entity.DisplayName,
    Backend: entity.Backend,
    Os: entity.Os,
    Enabled: entity.Enabled,
    AllowSsh: entity.AllowSsh,
    Labels: labels,
    Address: entity.Address,
    CpuPercent: entity.CpuPercent,
    MemUsedMb: entity.MemUsedMb,
    MemTotalMb: entity.MemTotalMb);
```

### SessionEventKind Addition
```csharp
// Add to SessionEventKind enum in Models.cs:
HostMetrics  // After CleanupCompleted
```

### SSH Metric Collection Commands
```csharp
// Windows (PowerShell):
// CPU: "(Get-Counter '\\Processor(_Total)\\% Processor Time').CounterSamples[0].CookedValue"
// Memory: "systeminfo | Select-String 'Total Physical Memory','Available Physical Memory'"
// Or more reliably:
// "$os = Get-CimInstance Win32_OperatingSystem; \"$($os.TotalVisibleMemorySize/1024)|$(($os.TotalVisibleMemorySize - $os.FreePhysicalMemory)/1024)\""

// Linux:
// CPU: "grep 'cpu ' /proc/stat | awk '{usage=($2+$4)*100/($2+$4+$5)} END {print usage}'"
// Memory: "free -m | awk '/Mem:/ {print $2\"|\"$3}'"

// macOS:
// CPU: "top -l 1 -n 0 | grep 'CPU usage' | awk '{print $3}' | tr -d '%'"
// Memory: "vm_stat | awk '/Pages active|Pages wired/ {sum+=$NF} END {print sum*4096/1048576}'"
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Full reload on SSE event | In-place state patching | Phase 6 | Eliminates redundant API calls, smoother UI |
| No host metrics | SSH poll every 30s | Phase 6 | MON-02 requirement fulfilled |
| No session input UI | CLI command + Web input bar | Phase 6 | AGENT-03/04 end-to-end path complete |

## Open Questions

1. **HostMetrics SSE event persistence**
   - What we know: DurableEventService persists ALL events to DB. HostMetrics events every 30s per host will accumulate rapidly.
   - What's unclear: Should HostMetrics events be persisted to DB or only broadcast to live SSE subscribers?
   - Recommendation: Broadcast only (skip DB persistence) since metrics are transient. This requires either a separate broadcast path in DurableEventService or a new lightweight broadcast-only method. Alternatively, persist but add a cleanup job. For v1.0, broadcast-only is simpler.

2. **EF Core Migration for HostEntity columns**
   - What we know: HostEntity needs 4 new columns (CpuPercent, MemUsedMb, MemTotalMb, MetricsUpdatedUtc).
   - What's unclear: Whether to use EF migrations or rely on EnsureCreated (SQLite dev mode).
   - Recommendation: The project uses SQLite with `EnsureCreated`. Simply add columns to entity and recreate DB on first run. If migration needed, `dotnet ef migrations add AddHostMetrics`.

3. **Console.KeyAvailable in Watch Mode**
   - What we know: Console.KeyAvailable checks for input without blocking. Spectre.Console Live context refreshes on a timer.
   - What's unclear: Whether Console.KeyAvailable works reliably inside Spectre.Console Live's async enumeration loop.
   - Recommendation: Check Console.KeyAvailable inside the await foreach loop, before processing each event. If 'i' detected, set flag and return from Live context (same as approval pattern).

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xunit 2.9.x + Microsoft.NET.Test.Sdk 17.x |
| Config file | tests/AgentHub.Tests/AgentHub.Tests.csproj |
| Quick run command | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~AgentHub.Tests" --no-build -v q` |
| Full suite command | `dotnet test tests/AgentHub.Tests -v q` |

### Phase Requirements to Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| AGENT-03 | CLI SendInputAsync calls POST endpoint | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~ApiClientTests.SendInputAsync" --no-build -x` | Wave 0 |
| AGENT-03 | Web SendInputAsync calls POST endpoint | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~DashboardApiClientTests.SendInputAsync" --no-build -x` | Wave 0 |
| AGENT-04 | Input routes through sanitization | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SessionCoordinatorTests" --no-build -x` | Existing (SessionCoordinatorTests.cs) |
| MON-02 | Host metrics stored in HostEntity | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~HostMetricTests" --no-build -x` | Wave 0 |
| MON-02 | HostMetrics SSE event emitted | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~HostMetricTests.EmitsHostMetricsEvent" --no-build -x` | Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test tests/AgentHub.Tests --no-build -v q`
- **Per wave merge:** `dotnet test tests/AgentHub.Tests -v q`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `tests/AgentHub.Tests/ApiClientTests.cs` -- add SendInputAsync test methods (file exists, tests don't)
- [ ] `tests/AgentHub.Tests/DashboardApiClientTests.cs` -- add SendInputAsync test methods (file exists, tests don't)
- [ ] `tests/AgentHub.Tests/HostMetricTests.cs` -- new file for metric polling and SSE emission tests

## Sources

### Primary (HIGH confidence)
- Codebase analysis -- all files directly inspected:
  - `src/AgentHub.Cli/Api/AgentHubApiClient.cs` -- CLI API client pattern
  - `src/AgentHub.Web/Services/DashboardApiClient.cs` -- Web API client pattern
  - `src/AgentHub.Cli/Commands/Session/SessionWatchCommand.cs` -- Watch command with approval exit pattern
  - `src/AgentHub.Cli/Api/SseStreamReader.cs` -- CS8602 warning source (line 83)
  - `src/AgentHub.Web/Components/Pages/FleetOverview.razor` -- Current full-reload SSE handler
  - `src/AgentHub.Web/Components/Pages/SessionDetail.razor` -- Where input bar goes
  - `src/AgentHub.Web/Components/Shared/HostSidebar.razor` -- Already renders metrics when present
  - `src/AgentHub.Orchestration/Data/Entities/HostEntity.cs` -- Missing metric columns
  - `src/AgentHub.Orchestration/Data/EntityMappers.cs` -- Missing metric field mapping
  - `src/AgentHub.Orchestration/HostDaemon/HostDaemonModels.cs` -- HostStatusReport missing MemoryTotalMb
  - `src/AgentHub.Orchestration/Events/DurableEventService.cs` -- Event emission pattern
  - `src/AgentHub.Orchestration/Monitoring/SessionMonitorService.cs` -- BackgroundService pattern
  - `src/AgentHub.Contracts/Models.cs` -- SendInputRequest DTO, SessionEventKind enum, HostRecord
  - `src/AgentHub.Service/Program.cs` -- POST input endpoint (lines 163-167), approval endpoint (line 184)

### Secondary (MEDIUM confidence)
- `.planning/phases/06-client-wiring-and-polish/06-CONTEXT.md` -- User decisions
- `.planning/v1.0-MILESTONE-AUDIT.md` -- CS8602 at SseStreamReader.cs line 83 confirmed
- `.planning/phases/03-cli-client/03-VERIFICATION.md` -- CS8602 description: "EventId.ToString() on possibly null"

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- all libraries already in use, no new dependencies
- Architecture: HIGH -- all patterns derived from existing codebase inspection
- Pitfalls: HIGH -- identified from direct code inspection (missing entity fields, nullable issue, Live context constraints)
- Metrics collection: MEDIUM -- SSH command specifics for Windows Get-Counter/CimInstance may need runtime validation

**Research date:** 2026-03-09
**Valid until:** 2026-04-09 (stable -- no external dependency changes expected)
