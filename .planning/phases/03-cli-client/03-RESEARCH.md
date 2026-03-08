# Phase 3: CLI Client - Research

**Researched:** 2026-03-08
**Domain:** .NET CLI application with rich TUI, SSE streaming, HTTP API consumption
**Confidence:** HIGH

## Summary

Phase 3 builds a standalone .NET console application (`ah`) that serves as the primary interface for the AgentHub platform. The CLI communicates with the existing AgentHub.Service REST API and SSE endpoints to manage sessions, monitor hosts, and handle approvals. The technology stack is well-established: System.CommandLine 2.0 (stable, shipped with .NET 10) for command parsing, Spectre.Console 0.54.0 for rich terminal output (tables, live displays, panels), and System.Net.ServerSentEvents 10.0.0 for consuming SSE streams.

The existing codebase provides all the server-side infrastructure: full REST API (sessions CRUD, hosts, skills, policy, approvals), per-session and fleet-wide SSE endpoints, and well-defined DTOs in AgentHub.Contracts. The CLI is a pure HTTP client -- it does NOT reference AgentHub.Orchestration or AgentHub.Service directly. It only depends on AgentHub.Contracts for shared DTOs and communicates via HTTP/SSE to the running server.

**Primary recommendation:** Create a new `AgentHub.Cli` console project referencing only `AgentHub.Contracts`. Use System.CommandLine 2.0 for command tree definition and Spectre.Console for all rendering. Build a thin `AgentHubApiClient` that wraps HttpClient for REST calls and uses `SseParser<string>` from System.Net.ServerSentEvents for event streaming.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- Tool name: `ah` (short, fast to type, like `gh` for GitHub)
- Primary structure: noun-verb groups (`ah session start`, `ah host list`, `ah config show`)
- Top-level shortcuts for frequent commands: `ah run` = `ah session start`, `ah ls` = `ah session list`
- Three resource groups: `session`, `host`, `config` -- approvals handled inline during session watch, not a separate group
- Launch syntax: positional agent type + optional `--host` flag. `ah run claude "fix the login bug" --host dev-box`
- Prompt as second positional arg or `--prompt/-p` flag
- Auto-placement when `--host` omitted (existing SimplePlacementEngine)
- Default output: compact columnar tables (like `docker ps`, `kubectl get`)
- Scriptable mode: `--json` flag on any command for machine-readable JSON output -- no colors, no spinners
- Exit codes: 0=success, 1=error, 2=timeout
- Rich TUI elements via Spectre.Console: colored tables, spinners, progress bars, emoji status indicators
- Colors on by default, `--no-color` flag and NO_COLOR env var to disable
- Three verbosity levels: default (essential info), `-v` (detail: timing, IDs), `-q` (errors only)
- `ah session watch <id>`: Live dashboard panel -- Spectre.Console Live display with status header and scrolling log
- Events color-coded by type (stdout=default, stderr=red, state=yellow)
- `ah run` auto-attaches to output by default; `--detach/-d` flag for background launch
- Ctrl+C detaches without stopping session (session keeps running)
- `ah session logs <id>`: Last 100 lines by default, `--all` for full history, `--tail N` to customize, `--follow/-f` to attach if still running
- Single-session watch for individual detail
- Fleet overview: `ah watch` (no ID) shows live table of all running sessions with status updates
- Enter and leave sessions without terminating them
- Approval requests during session watch: auto-popup Spectre.Console panel overlaying watch output
- Approval queue modes -- toggleable with keypress + visual indicator: FIFO, Pick-one, Per-session
- Background notifications: terminal bell when events arrive + pending notification summary on next `ah` command
- Background listener: `ah listen` runs in dedicated terminal, prints notification stream
- Resource monitoring: `ah host status --watch` uses Spectre.Console Live to refresh CPU/memory metrics

### Claude's Discretion
- System.CommandLine vs Spectre.Console.Cli for command parsing (both viable in .NET)
- Exact Spectre.Console component choices (Table, Live, Status, Panel, etc.)
- HTTP client implementation for API communication (reuse patterns from MAUI ApiClient or fresh)
- SSE client implementation for event streaming
- Config file location and format for CLI settings (server URL, default preferences)
- Exact color palette and status indicator symbols

### Deferred Ideas (OUT OF SCOPE)
- All-flags launch mode (`--agent claude --host dev-box --prompt "..."`) -- backlog for scripting ergonomics
- Profile-based launch (`ah run my-task-profile`) -- backlog for reusable task definitions
- Split-pane multi-session watch -- release candidate level, not v1
- OS-level toast notifications -- implement with web/app client (Phase 4+)
- `ah approval` as separate resource group -- if approval workflows get complex enough to warrant it
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| CLI-01 | CLI client supports launching, monitoring, and managing sessions | System.CommandLine 2.0 command tree with session CRUD commands; HttpClient calling existing REST API endpoints; Spectre.Console for table/live output |
| CLI-02 | CLI supports both interactive and scriptable (non-interactive) modes | `--json` flag for machine-readable output; exit codes 0/1/2; `--no-color`/NO_COLOR; `-q` quiet mode; IOutputFormatter abstraction |
| MON-02 | User can view resource usage (CPU, memory) per host | `ah host status` command hitting `/api/hosts` endpoint; `--watch` flag using Spectre.Console Live display with periodic refresh |
| MON-03 | User receives notifications for session completion, errors, and approval requests | SSE consumption via System.Net.ServerSentEvents; `ah listen` background stream; terminal bell; notification summary persistence; approval inline popup during watch |
</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| System.CommandLine | 2.0.0+ | CLI command parsing | Stable release shipped Nov 2025 with .NET 10. First-party Microsoft. NativeAOT support. Current NuGet shows 2.0.3. |
| Spectre.Console | 0.54.0 | Rich terminal output | Tables, Live display, Panel, Status, Progress. Mature, 9k+ GitHub stars. Handles NO_COLOR detection. |
| System.Net.ServerSentEvents | 10.0.0 | SSE client parsing | Ships with .NET 10 runtime. SseParser + SseItem for typed SSE consumption. Handles spec compliance. |
| AgentHub.Contracts | (project ref) | Shared DTOs | SessionSummary, HostRecord, SessionEvent, StartSessionRequest -- all models already defined |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| System.Text.Json | 10.0.x (built-in) | JSON serialization | All API request/response serialization. Use JsonSerializerDefaults.Web for camelCase consistency with server. |
| Microsoft.Extensions.Configuration | 10.0.x | CLI config loading | For reading server URL and preferences from config file |
| Microsoft.Extensions.DependencyInjection | 10.0.x | DI container | Service registration for ApiClient, formatters, config |
| Polly | 8.x | HTTP resilience | Retry on transient HTTP failures to AgentHub.Service |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| System.CommandLine | Spectre.Console.Cli | Spectre.Console.Cli has tighter rendering integration but is not first-party. System.CommandLine is Microsoft-blessed, stable, and composes well with Spectre for output. Use System.CommandLine. |
| Raw HttpClient | Refit | Refit adds compile-time HTTP client generation but is another dependency. The API surface is small enough (15 endpoints) that a hand-written typed client is simpler and more transparent. |
| System.Net.ServerSentEvents | LaunchDarkly.EventSource | Third-party SSE library with reconnection built-in. Not needed -- System.Net.ServerSentEvents is first-party and we handle reconnection ourselves. |

**Installation:**
```bash
# Create new console project
dotnet new console -n AgentHub.Cli -o src/AgentHub.Cli --framework net10.0

# Add packages
cd src/AgentHub.Cli
dotnet add package System.CommandLine --version 2.0.3
dotnet add package Spectre.Console --version 0.54.0
dotnet add package System.Net.ServerSentEvents --version 10.0.0
dotnet add package Microsoft.Extensions.Configuration.Json --version 10.0.0
dotnet add package Microsoft.Extensions.DependencyInjection --version 10.0.0
dotnet add package Polly --version 8.5.2

# Add project reference
dotnet add reference ../AgentHub.Contracts/AgentHub.Contracts.csproj
```

## Architecture Patterns

### Recommended Project Structure
```
src/AgentHub.Cli/
├── Program.cs                     # Entry point, command tree setup
├── AgentHub.Cli.csproj
├── Commands/
│   ├── Session/
│   │   ├── SessionCommand.cs      # `ah session` parent command
│   │   ├── SessionStartCommand.cs # `ah session start` / `ah run`
│   │   ├── SessionListCommand.cs  # `ah session list` / `ah ls`
│   │   ├── SessionStopCommand.cs  # `ah session stop`
│   │   ├── SessionWatchCommand.cs # `ah session watch <id>`
│   │   └── SessionLogsCommand.cs  # `ah session logs <id>`
│   ├── Host/
│   │   ├── HostCommand.cs         # `ah host` parent command
│   │   └── HostStatusCommand.cs   # `ah host list`, `ah host status`
│   ├── Config/
│   │   └── ConfigShowCommand.cs   # `ah config show`
│   ├── WatchCommand.cs            # `ah watch` (fleet overview)
│   └── ListenCommand.cs           # `ah listen` (notification stream)
├── Api/
│   ├── AgentHubApiClient.cs       # Typed HTTP client for REST endpoints
│   └── SseStreamReader.cs         # SSE consumption using SseParser
├── Output/
│   ├── IOutputFormatter.cs        # Abstraction for table vs JSON output
│   ├── TableFormatter.cs          # Spectre.Console table rendering
│   ├── JsonFormatter.cs           # Raw JSON output for scripting
│   └── LiveDisplayManager.cs      # Manages Spectre.Console Live contexts
├── Notifications/
│   ├── NotificationService.cs     # Handles bell, pending summary
│   └── ApprovalPromptHandler.cs   # Inline approval panel during watch
├── Config/
│   └── CliConfig.cs               # CLI settings (server URL, preferences)
└── appsettings.json               # Default config (server URL, etc.)
```

### Pattern 1: Command Tree with System.CommandLine 2.0
**What:** Define commands using `CliRootCommand`, `CliCommand`, `CliArgument<T>`, `CliOption<T>`. Each command has a handler action.
**When to use:** All command definitions.
**Example:**
```csharp
// System.CommandLine 2.0 stable API
var rootCommand = new CliRootCommand("AgentHub CLI - manage agent sessions across your fleet");

// Global options available on all commands
var jsonOption = new CliOption<bool>("--json") { Description = "Output as JSON" };
var noColorOption = new CliOption<bool>("--no-color") { Description = "Disable colored output" };
var verboseOption = new CliOption<bool>("-v", "--verbose") { Description = "Verbose output" };
var quietOption = new CliOption<bool>("-q", "--quiet") { Description = "Errors only" };
rootCommand.Options.Add(jsonOption);
rootCommand.Options.Add(noColorOption);
rootCommand.Options.Add(verboseOption);
rootCommand.Options.Add(quietOption);

// Session subcommand group
var sessionCommand = new CliCommand("session", "Manage agent sessions");
var startCommand = new CliCommand("start", "Start a new agent session");
var agentArg = new CliArgument<string>("agent") { Description = "Agent type (e.g., claude)" };
var promptArg = new CliArgument<string>("prompt") { Description = "Task prompt" };
startCommand.Arguments.Add(agentArg);
startCommand.Arguments.Add(promptArg);
startCommand.SetAction((parseResult, ct) =>
{
    var agent = parseResult.GetValue(agentArg);
    var prompt = parseResult.GetValue(promptArg);
    // ... call API
    return Task.CompletedTask;
});
sessionCommand.Subcommands.Add(startCommand);
rootCommand.Subcommands.Add(sessionCommand);

// Top-level shortcut: `ah run` = `ah session start`
var runCommand = new CliCommand("run", "Start a session (shortcut for 'session start')");
runCommand.Arguments.Add(agentArg);
runCommand.Arguments.Add(promptArg);
runCommand.SetAction(startCommand.Action!);
rootCommand.Subcommands.Add(runCommand);
```

### Pattern 2: Output Formatter Abstraction
**What:** IOutputFormatter interface that commands use for output, switching between rich Spectre.Console tables and plain JSON based on `--json` flag.
**When to use:** Every command that produces output.
**Example:**
```csharp
public interface IOutputFormatter
{
    void WriteTable<T>(IEnumerable<T> items, Action<Table, T> addRow, params string[] columns);
    void WriteObject<T>(T item);
    void WriteError(string message);
    void WriteSuccess(string message);
}

public sealed class TableFormatter : IOutputFormatter
{
    public void WriteTable<T>(IEnumerable<T> items, Action<Table, T> addRow, params string[] columns)
    {
        var table = new Table();
        foreach (var col in columns)
            table.AddColumn(col);
        foreach (var item in items)
            addRow(table, item);
        AnsiConsole.Write(table);
    }
    // ...
}

public sealed class JsonFormatter : IOutputFormatter
{
    public void WriteTable<T>(IEnumerable<T> items, Action<Table, T> addRow, params string[] columns)
    {
        Console.WriteLine(JsonSerializer.Serialize(items, JsonOpts));
    }
    // ...
}
```

### Pattern 3: SSE Stream Consumption with SseParser
**What:** Use System.Net.ServerSentEvents `SseParser.Create()` to consume SSE streams from the server.
**When to use:** `ah session watch`, `ah watch`, `ah listen`, `--follow` on logs.
**Example:**
```csharp
// Source: System.Net.ServerSentEvents 10.0.0 API + Microsoft Learn docs
public async IAsyncEnumerable<SessionEvent> StreamSessionEventsAsync(
    string sessionId, string? lastEventId, [EnumeratorCancellation] CancellationToken ct)
{
    var request = new HttpRequestMessage(HttpMethod.Get, $"/api/sessions/{sessionId}/events");
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
    if (lastEventId is not null)
        request.Headers.Add("Last-Event-ID", lastEventId);

    using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    response.EnsureSuccessStatusCode();
    await using var stream = await response.Content.ReadAsStreamAsync(ct);

    await foreach (var sseItem in SseParser.Create(stream).EnumerateAsync(ct))
    {
        var evt = JsonSerializer.Deserialize<SessionEvent>(sseItem.Data, JsonOpts);
        if (evt is not null) yield return evt;
    }
}
```

### Pattern 4: Spectre.Console Live Display for Watch
**What:** Use `AnsiConsole.Live()` to render a continuously updating dashboard for session watching.
**When to use:** `ah session watch <id>`, `ah watch`, `ah host status --watch`.
**Example:**
```csharp
await AnsiConsole.Live(layout)
    .AutoClear(false)
    .Overflow(VerticalOverflow.Ellipsis)
    .StartAsync(async ctx =>
    {
        await foreach (var evt in apiClient.StreamSessionEventsAsync(sessionId, null, ct))
        {
            UpdateLayout(layout, evt);  // Update the renderable
            ctx.Refresh();              // Redraw
        }
    });
```

### Anti-Patterns to Avoid
- **Referencing AgentHub.Orchestration from CLI:** The CLI is a pure HTTP client. It should ONLY reference AgentHub.Contracts for DTOs. Never import Orchestration types -- that would couple the CLI to server internals.
- **Mixing Spectre.Console Live with Console.WriteLine:** Live display owns the terminal. All output during a Live context must go through the Live display's renderable update, not Console.WriteLine.
- **Blocking the SSE stream on the UI thread:** SSE consumption should run on a background task. UI updates (Spectre Live refresh) happen on the main thread. Use `Channel<T>` or async enumerable to bridge.
- **Hardcoding server URL:** Always read from config file with a sensible default (http://localhost:5000).

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| SSE parsing | Custom text/event-stream parser | System.Net.ServerSentEvents `SseParser` | SSE spec has edge cases: multi-line data, retry, last-event-id, comment lines. The BCL parser handles all of them. |
| Terminal tables | Manual column-width calculation + Console.Write | Spectre.Console `Table` | Unicode width, column wrapping, color markup, border styles -- all handled. |
| Live terminal updates | ANSI escape code manipulation | Spectre.Console `Live` | Cursor positioning, screen clearing, partial redraws -- extremely error-prone to hand-roll across terminal types. |
| Command-line parsing | Custom argv parsing | System.CommandLine 2.0 | Subcommands, options, arguments, help generation, tab completion, response files -- massive surface area. |
| JSON camelCase | Custom naming or attribute decoration | `JsonSerializerDefaults.Web` | Matches server-side convention. One-line setup. |
| Color detection | Manual TERM/COLORTERM checks | Spectre.Console auto-detection + NO_COLOR | Spectre.Console auto-detects terminal capabilities and respects NO_COLOR convention. |

**Key insight:** The CLI is a thin presentation layer over an HTTP API. Nearly all complexity is in rendering (Spectre.Console) and streaming (SSE). Both have excellent library support -- focus implementation effort on UX flow, not infrastructure.

## Common Pitfalls

### Pitfall 1: Spectre.Console Live Display Thread Safety
**What goes wrong:** Live display is NOT thread safe. If SSE events arrive on a background thread and directly call `ctx.Refresh()`, you get corrupted terminal output.
**Why it happens:** HttpClient streams run on thread pool threads. Spectre.Console Live expects single-threaded access.
**How to avoid:** Use an async pattern where the SSE consumer feeds events into a Channel<T>, and the Live display loop reads from the channel on a single thread.
**Warning signs:** Garbled terminal output, flickering, duplicate lines.

### Pitfall 2: SSE Reconnection Not Automatic
**What goes wrong:** HttpClient SSE stream breaks on network hiccup. Without reconnection logic, the CLI silently stops receiving events.
**Why it happens:** Unlike browser EventSource, HttpClient has no built-in reconnection. System.Net.ServerSentEvents is a parser, not a transport manager.
**How to avoid:** Wrap SSE consumption in a retry loop. Track `LastEventId` from each received event. On disconnect, reconnect with `Last-Event-ID` header for seamless replay. The server already supports this (SseItem EventId set to DB auto-increment Id).
**Warning signs:** Watch display freezes, no new events appear, no error message shown.

### Pitfall 3: Ctrl+C Handling Conflicts
**What goes wrong:** Default Ctrl+C kills the CLI process. User expectation is that Ctrl+C detaches from a watched session WITHOUT stopping it.
**Why it happens:** .NET Console.CancelKeyPress fires by default. Spectre.Console may also hook this.
**How to avoid:** Set `Console.CancelKeyPress += (_, e) => { e.Cancel = true; /* set detach flag */ };` Alternatively, use `Console.TreatControlCAsInput = true` during watch mode and detect Ctrl+C via `Console.ReadKey()`. Use CancellationTokenSource to cleanly tear down the SSE stream.
**Warning signs:** Ctrl+C kills the whole CLI instead of detaching. Or Ctrl+C does nothing at all.

### Pitfall 4: JSON Output Mode Leaking Rich Formatting
**What goes wrong:** A command uses `AnsiConsole.MarkupLine()` before checking `--json` flag. Piped output contains ANSI escape codes.
**Why it happens:** Commands directly call Spectre.Console instead of going through the output formatter abstraction.
**How to avoid:** ALL command output MUST go through `IOutputFormatter`. The formatter implementation checks the `--json` flag. Never call `AnsiConsole.Write*` directly from command handlers.
**Warning signs:** `ah ls --json | jq .` fails with parse errors.

### Pitfall 5: Exit Code Not Set on Error
**What goes wrong:** CLI returns 0 even when an API call fails. Automation scripts can't detect errors.
**Why it happens:** System.CommandLine handlers return Task (void). The exit code must be explicitly set.
**How to avoid:** Command handlers should return `int` (or use `SetAction` with `ParseResult` and set `parseResult.Configuration.Output` + return code). Catch HttpRequestException and map to exit code 1. Catch OperationCanceledException (timeout) and map to exit code 2.
**Warning signs:** `ah session stop nonexistent && echo "success"` prints "success".

### Pitfall 6: Approval Panel Blocking Watch Updates
**What goes wrong:** When an approval request arrives during session watch, showing a Spectre.Console prompt blocks the Live display, causing buffered events to pile up.
**Why it happens:** Spectre.Console prompts and Live displays cannot coexist -- both need terminal control.
**How to avoid:** When approval arrives: (1) stop Live display, (2) show approval panel as a static render, (3) collect user input with a simple prompt, (4) POST approval resolution, (5) restart Live display. Buffer incoming events during the approval interaction.
**Warning signs:** Terminal freezes during approval, events lost.

## Code Examples

### AgentHubApiClient (REST wrapper)
```csharp
// Typed HTTP client for AgentHub.Service REST API
public sealed class AgentHubApiClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public AgentHubApiClient(HttpClient http) => _http = http;

    public async Task<(List<SessionSummary> Items, int TotalCount)> GetSessionsAsync(
        int? skip = null, int? take = null, string? state = null, CancellationToken ct = default)
    {
        var url = $"/api/sessions?skip={skip ?? 0}&take={take ?? 50}";
        if (state is not null) url += $"&state={state}";
        var result = await _http.GetFromJsonAsync<SessionListResponse>(url, JsonOpts, ct);
        return (result?.Items ?? [], result?.TotalCount ?? 0);
    }

    public async Task<SessionSummary?> GetSessionAsync(string sessionId, CancellationToken ct = default)
        => await _http.GetFromJsonAsync<SessionSummary>($"/api/sessions/{sessionId}", JsonOpts, ct);

    public async Task<string> StartSessionAsync(StartSessionRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/sessions", req, JsonOpts, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<StartSessionResponse>(JsonOpts, ct);
        return result!.SessionId;
    }

    public async Task StopSessionAsync(string sessionId, bool force = false, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/sessions/{sessionId}?force={force}", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<HostRecord>> GetHostsAsync(CancellationToken ct = default)
        => await _http.GetFromJsonAsync<List<HostRecord>>("/api/hosts", JsonOpts, ct) ?? [];

    public async Task ResolveApprovalAsync(string approvalId, bool approved, CancellationToken ct = default)
    {
        var req = new { approved, resolvedBy = "cli-user" };
        var response = await _http.PostAsJsonAsync($"/api/approvals/{approvalId}/resolve", req, JsonOpts, ct);
        response.EnsureSuccessStatusCode();
    }

    // SSE streaming
    public async IAsyncEnumerable<(string EventId, SessionEvent Event)> StreamSessionEventsAsync(
        string sessionId, string? lastEventId, [EnumeratorCancellation] CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/sessions/{sessionId}/events");
        if (lastEventId is not null)
            request.Headers.Add("Last-Event-ID", lastEventId);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);

        await foreach (var item in SseParser.Create(stream).EnumerateAsync(ct))
        {
            var evt = JsonSerializer.Deserialize<SessionEvent>(item.Data, JsonOpts);
            if (evt is not null)
                yield return (item.EventId, evt);
        }
    }

    private sealed record SessionListResponse(List<SessionSummary> Items, int TotalCount);
    private sealed record StartSessionResponse(string SessionId);
}
```

### CLI Config File Pattern
```csharp
// ~/.agenthub/config.json (or platform-appropriate location)
public sealed class CliConfig
{
    public string ServerUrl { get; set; } = "http://localhost:5000";
    public string? DefaultHost { get; set; }
    public string? DefaultAgent { get; set; } = "claude";
    public int WatchRefreshMs { get; set; } = 2000;
    public int LogTailDefault { get; set; } = 100;

    public static CliConfig Load()
    {
        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".agenthub");
        var configPath = Path.Combine(configDir, "config.json");

        if (!File.Exists(configPath))
            return new CliConfig();

        var json = File.ReadAllText(configPath);
        return JsonSerializer.Deserialize<CliConfig>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? new CliConfig();
    }
}
```

### Session List Table Output
```csharp
// Spectre.Console table for `ah ls` / `ah session list`
public static void RenderSessionTable(IEnumerable<SessionSummary> sessions)
{
    var table = new Table()
        .Border(TableBorder.Rounded)
        .AddColumn("ID")
        .AddColumn("State")
        .AddColumn("Agent")
        .AddColumn("Host")
        .AddColumn("Created")
        .AddColumn("Duration");

    foreach (var s in sessions)
    {
        var stateColor = s.State switch
        {
            SessionState.Running => "green",
            SessionState.Failed => "red",
            SessionState.Stopped => "grey",
            SessionState.Pending => "yellow",
            _ => "white"
        };

        table.AddRow(
            s.SessionId[..8],  // Short ID
            $"[{stateColor}]{s.State}[/]",
            s.Requirements.ToString() ?? "-",
            s.Node ?? "(auto)",
            s.CreatedUtc.LocalDateTime.ToString("HH:mm:ss"),
            (DateTimeOffset.UtcNow - s.CreatedUtc).ToString(@"hh\:mm\:ss")
        );
    }

    AnsiConsole.Write(table);
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| System.CommandLine beta4 | System.CommandLine 2.0 stable | Nov 2025 | API stabilized. `CliRootCommand`/`CliCommand`/`CliOption<T>` replaced `RootCommand`/`Command`/`Option<T>`. SetAction replaces SetHandler. |
| Custom SSE parsing with StreamReader | System.Net.ServerSentEvents `SseParser` | .NET 9+ (v9.0.0) | BCL-native SSE parser. Handles spec edge cases. Available as 10.0.0 for .NET 10. |
| Spectre.Console 0.48 | Spectre.Console 0.54.0 | Nov 2025 | Performance improvements, new widgets, better Live display. |

**Deprecated/outdated:**
- `RootCommand`, `Command`, `Option<T>` from System.CommandLine beta: Use `CliRootCommand`, `CliCommand`, `CliOption<T>` in 2.0 stable
- `SetHandler()` extension method: Use `SetAction()` in 2.0 stable
- Manual SSE line parsing with StreamReader: Use SseParser.Create() from System.Net.ServerSentEvents

## Open Questions

1. **Host resource metrics endpoint**
   - What we know: `GET /api/hosts` returns HostRecord with basic info (HostId, DisplayName, Os, etc.)
   - What's unclear: HostRecord does not currently include CPU/memory fields. MON-02 requires resource usage display.
   - Recommendation: The server needs a resource metrics endpoint or enriched host response. Either add fields to HostRecord (CpuPercent, MemUsedMb, MemTotalMb) or create a new `/api/hosts/{id}/metrics` endpoint. The CLI implementation should assume the API will provide this data -- the planner should include a server-side task for adding the metrics endpoint.

2. **Notification persistence between CLI invocations**
   - What we know: "Pending notification summary shown on next `ah` command" is a locked decision.
   - What's unclear: Where to persist notifications between CLI runs. Options: local file in `~/.agenthub/`, or query server for unacknowledged events.
   - Recommendation: Use a local notification cache file (`~/.agenthub/notifications.json`). The `ah listen` daemon writes to it, and other `ah` commands check/clear it on startup. Simpler than server-side notification state.

3. **System.CommandLine 2.0 exact stable API**
   - What we know: NuGet shows version 2.0.3. The API changed significantly from beta4 to stable.
   - What's unclear: Some API surface details may differ between documentation and the actual 2.0.3 package.
   - Recommendation: The core pattern (CliRootCommand, CliCommand, CliOption, SetAction) is confirmed by Microsoft Learn docs and NuGet. HIGH confidence for the patterns documented above.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.x (existing project) |
| Config file | `tests/AgentHub.Tests/AgentHub.Tests.csproj` (existing) |
| Quick run command | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~Cli" -x` |
| Full suite command | `dotnet test tests/AgentHub.Tests` |

### Phase Requirements to Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| CLI-01 | CLI commands invoke correct API endpoints | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~CliCommandTests" --no-build` | No -- Wave 0 |
| CLI-01 | Session start/stop/list returns expected data | integration | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~CliIntegrationTests" --no-build` | No -- Wave 0 |
| CLI-02 | JSON output is valid JSON with correct structure | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~JsonFormatterTests" --no-build` | No -- Wave 0 |
| CLI-02 | Exit codes map correctly (0=success, 1=error, 2=timeout) | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~ExitCodeTests" --no-build` | No -- Wave 0 |
| MON-02 | Host status displays resource metrics | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~HostStatusTests" --no-build` | No -- Wave 0 |
| MON-03 | SSE events are received and parsed correctly | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SseStreamTests" --no-build` | No -- Wave 0 |
| MON-03 | Approval events trigger prompt display | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~ApprovalHandlerTests" --no-build` | No -- Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~Cli" --no-build`
- **Per wave merge:** `dotnet test tests/AgentHub.Tests`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `tests/AgentHub.Tests/CliCommandTests.cs` -- covers CLI-01 (command routing, API client calls)
- [ ] `tests/AgentHub.Tests/JsonFormatterTests.cs` -- covers CLI-02 (JSON output correctness)
- [ ] `tests/AgentHub.Tests/ExitCodeTests.cs` -- covers CLI-02 (exit code mapping)
- [ ] `tests/AgentHub.Tests/SseStreamTests.cs` -- covers MON-03 (SSE consumption parsing)
- [ ] `tests/AgentHub.Tests/ApprovalHandlerTests.cs` -- covers MON-03 (approval event handling)
- [ ] Add `AgentHub.Cli` project reference to test project
- [ ] Note: Spectre.Console rendering tests are hard to automate. Test the data layer (formatter input/output) not the terminal rendering itself. Use `TestConsole` from Spectre.Console.Testing package if needed.

## Sources

### Primary (HIGH confidence)
- [NuGet: System.CommandLine 2.0.3](https://www.nuget.org/packages/System.CommandLine) - stable version confirmed
- [NuGet: System.Net.ServerSentEvents 10.0.0](https://www.nuget.org/packages/System.Net.ServerSentEvents) - BCL SSE parser
- [Microsoft Learn: System.CommandLine overview](https://learn.microsoft.com/en-us/dotnet/standard/commandline/) - API patterns, tutorial
- [Microsoft Learn: System.CommandLine migration to beta5+](https://learn.microsoft.com/en-us/dotnet/standard/commandline/migration-guide-2.0.0-beta5) - API changes from beta to stable
- [Microsoft Learn: SseParser API](https://learn.microsoft.com/en-us/dotnet/api/system.net.serversentevents.sseparser?view=net-10.0) - SseParser usage
- [Spectre.Console Live Display](https://spectreconsole.net/live/live-display) - Live display API and thread safety notes
- [Spectre.Console Documentation](https://spectreconsole.net/) - Table, Panel, Status, Progress widgets
- Existing codebase: `AgentHub.Contracts/Models.cs`, `AgentHub.Service/Program.cs`, `AgentHub.Maui/ApiClient.cs` -- direct code inspection

### Secondary (MEDIUM confidence)
- [Strathweb: Built-in SSE support in .NET 9](https://www.strathweb.com/2024/07/built-in-support-for-server-sent-events-in-net-9/) - SseParser usage patterns
- [Milan Jovanovic: SSE in ASP.NET Core and .NET 10](https://www.milanjovanovic.tech/blog/server-sent-events-in-aspnetcore-and-dotnet-10) - server+client patterns
- [NO_COLOR convention](https://no-color.org/) - environment variable standard
- [dotnet/command-line-api#2576](https://github.com/dotnet/command-line-api/issues/2576) - release plan and API changes

### Tertiary (LOW confidence)
- Stack research STACK.md -- version info verified at project start (2026-03-08), still current

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - All packages verified on NuGet, versions confirmed, APIs documented
- Architecture: HIGH - Patterns based on existing codebase conventions and verified library APIs
- Pitfalls: HIGH - Thread safety limitations documented in Spectre.Console official docs; SSE reconnection is well-known gap
- API surface: HIGH - Full endpoint list extracted directly from AgentHub.Service/Program.cs source

**Research date:** 2026-03-08
**Valid until:** 2026-04-08 (stable libraries, 30-day window)
