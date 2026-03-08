# Phase 4: Web Dashboard - Research

**Researched:** 2026-03-08
**Domain:** Blazor Server + MudBlazor + SSE consumption + Aspire integration
**Confidence:** HIGH

## Summary

Phase 4 adds a Blazor Server web dashboard (AgentHub.Web) that provides fleet oversight with live session status and real-time output streaming. The dashboard consumes the existing AgentHub.Service REST API and SSE endpoints -- the same ones the CLI already uses. The project targets .NET 10.0, uses MudBlazor 9.1.0 for Material Design components, and wires into the existing Aspire AppHost for service discovery.

The core technical challenges are: (1) consuming SSE streams from Blazor Server components via HttpClient without blocking the SignalR circuit, (2) rendering terminal-style output with color-coded lines and auto-scroll, and (3) structuring the Aspire AppHost to orchestrate both AgentHub.Service and AgentHub.Web with service discovery. All REST API patterns are already proven by the CLI client -- the Blazor project reuses the same DTOs from AgentHub.Contracts and mirrors the AgentHubApiClient pattern.

**Primary recommendation:** Create AgentHub.Web as a Blazor Server project referencing AgentHub.Contracts, use MudBlazor 9.1.0 for all UI components, consume the API via a typed HttpClient service with Aspire service discovery, and use a background service pattern with Channel<T> to bridge SSE streams to Blazor component state.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- Blazor Server (SignalR-based) hosting model
- Separate project: AgentHub.Web, references AgentHub.Contracts, calls AgentHub.Service via HTTP/SSE
- Wire into Aspire AppHost for service discovery
- MudBlazor component library for Material Design UI
- Split panel design: host sidebar left, session data table right
- Host sidebar: compact status cards with status dot, session count badge, CPU/memory mini-bars, click to filter
- Session panel: MudBlazor DataTable with status, agent type, prompt, host, duration, started columns. Sortable/filterable. Row click navigates to detail
- Real-time updates: periodic polling (5-10s) default, "Enable All-Session Updates" toggle for full SSE
- Session detail: dedicated /session/{id} page with metadata header and terminal-style output panel
- Terminal aesthetic: stdout white/green, stderr red, state changes yellow. Auto-scroll with scroll-lock
- Full history replay for completed sessions via paginated /api/sessions/{id}/history
- Full CRUD: monitor, launch, stop sessions
- Session launch: MudBlazor dialog with agent type, host selector, prompt, optional flags
- Stop sessions from table and detail page
- Inline approval handling via alerts/dialogs using /api/approvals/{id}/resolve
- Dark mode default, light mode toggle, MudBlazor theme support

### Claude's Discretion
- Exact MudBlazor component choices and layout breakpoints
- SSE client implementation for Blazor Server (SignalR circuit vs HttpClient-based)
- Polling interval and SSE toggle persistence (localStorage vs server-side)
- Terminal output rendering approach (pre-formatted text vs virtual scrolling)
- Aspire AppHost resource configuration details
- Navigation structure (sidebar nav vs top nav)
- Error handling and loading states

### Deferred Ideas (OUT OF SCOPE)
- OS-level toast notifications from the web dashboard
- Split-pane multi-session watch (multiple session outputs simultaneously)
- Dashboard-based config editing (skills, policies, agent definitions)
- Session diff review workflow (MON-05)
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| WEB-01 | Blazor web dashboard shows fleet overview with live session status | MudBlazor DataTable/DataGrid for session list, host sidebar with status cards, periodic polling + SSE toggle for live updates |
| WEB-02 | Web dashboard streams real-time agent output inline | SSE consumption via HttpClient + Channel<T> bridge, terminal-style output panel with color-coded rendering, auto-scroll behavior |
</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Blazor Server | .NET 10.0 | Web UI framework | User-decided. Server-side rendering with SignalR, ideal for real-time SSE consumption since HttpClient runs server-side |
| MudBlazor | 9.1.0 | Material Design component library | User-decided. Supports .NET 10, provides DataTable/DataGrid, dialogs, theming, layout components |
| Aspire.Hosting.AppHost | 9.0.0-preview.1 (current) | Service orchestration | Already in project. Provides service discovery between Web and Service |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| System.Net.ServerSentEvents | 10.0.3 | SSE parsing | Already used in CLI. SseParser for consuming SSE streams from API |
| Microsoft.Extensions.Http | (built-in) | Typed HttpClient factory | Service discovery + resilience for API calls |
| AgentHub.Contracts | (project ref) | Shared DTOs | All models (SessionSummary, SessionEvent, HostRecord, etc.) |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| MudBlazor DataTable | MudBlazor DataGrid | DataGrid has richer server-side features but DataTable is simpler. Use DataGrid for sessions (needs sorting/filtering), DataTable for simpler lists |
| HttpClient SSE | SignalR hub to Service | Would require new server-side hub -- unnecessary since SSE endpoints already exist and HttpClient runs server-side in Blazor Server |
| Periodic polling | WebSocket | SSE endpoints already built; polling is simpler default; SSE toggle gives real-time when needed |

**Installation:**
```bash
dotnet new blazor --interactivity Server --name AgentHub.Web -o src/AgentHub.Web
dotnet add src/AgentHub.Web/AgentHub.Web.csproj package MudBlazor --version 9.1.0
dotnet add src/AgentHub.Web/AgentHub.Web.csproj package System.Net.ServerSentEvents --version 10.0.3
dotnet add src/AgentHub.Web/AgentHub.Web.csproj reference src/AgentHub.Contracts/AgentHub.Contracts.csproj
dotnet sln AgentSafeEnv.sln add src/AgentHub.Web/AgentHub.Web.csproj
```

## Architecture Patterns

### Recommended Project Structure
```
src/AgentHub.Web/
├── Program.cs                    # DI, MudBlazor, HttpClient, Aspire service defaults
├── AgentHub.Web.csproj           # Project file
├── Components/
│   ├── App.razor                 # Root component with MudThemeProvider
│   ├── Routes.razor              # Router
│   ├── Layout/
│   │   ├── MainLayout.razor      # MudLayout with appbar, nav, theme toggle
│   │   └── NavMenu.razor         # Navigation menu
│   ├── Pages/
│   │   ├── FleetOverview.razor   # Landing page: host sidebar + session table
│   │   └── SessionDetail.razor   # /session/{id} with metadata + terminal output
│   └── Shared/
│       ├── HostSidebar.razor     # Host list with status cards
│       ├── SessionTable.razor    # MudDataGrid for sessions
│       ├── TerminalOutput.razor  # Terminal-style output panel
│       ├── LaunchDialog.razor    # Session launch modal
│       └── ApprovalAlert.razor   # Inline approval handling
├── Services/
│   ├── DashboardApiClient.cs     # Typed HttpClient for AgentHub.Service API
│   ├── SseStreamService.cs       # SSE consumption with Channel<T> bridge
│   └── ThemeService.cs           # Dark/light mode state
└── wwwroot/
    └── css/
        └── terminal.css          # Terminal output styling
```

### Pattern 1: Typed HttpClient with Aspire Service Discovery
**What:** Register a typed HttpClient for API calls, with Aspire providing the base URL via service discovery.
**When to use:** All REST API calls to AgentHub.Service.
**Example:**
```csharp
// Program.cs
builder.Services.AddHttpClient<DashboardApiClient>(client =>
{
    client.BaseAddress = new Uri("https+http://agenthub-service");
});

// DashboardApiClient.cs -- mirrors CLI's AgentHubApiClient
public sealed class DashboardApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    public async Task<(List<SessionSummary> Items, int TotalCount)> GetSessionsAsync(
        int? skip = null, int? take = null, string? state = null, CancellationToken ct = default)
    {
        // Same pattern as AgentHub.Cli.Api.AgentHubApiClient
    }
}
```

### Pattern 2: SSE Consumption via Background Channel Bridge
**What:** Consume SSE streams in a background task, push events through Channel<T>, components read from channel.
**When to use:** Real-time session output streaming and fleet-wide event updates.
**Why:** Blazor Server components run on the SignalR circuit's synchronization context. Long-running SSE streams must not block the circuit. Channel<T> decouples production (background HttpClient SSE read) from consumption (component rendering).
**Example:**
```csharp
// SseStreamService.cs
public sealed class SseStreamService(IHttpClientFactory httpFactory) : IDisposable
{
    public ChannelReader<SessionEvent> SubscribeSession(string sessionId, CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<SessionEvent>();
        _ = Task.Run(async () =>
        {
            var http = httpFactory.CreateClient("DashboardApi");
            var request = new HttpRequestMessage(HttpMethod.Get,
                $"/api/sessions/{sessionId}/events");
            request.Headers.Accept.ParseAdd("text/event-stream");

            var response = await http.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, ct);
            var stream = await response.Content.ReadAsStreamAsync(ct);

            await foreach (var sseItem in SseParser.Create(stream).EnumerateAsync(ct))
            {
                var data = sseItem.Data.ToString();
                if (string.IsNullOrWhiteSpace(data)) continue;
                var evt = JsonSerializer.Deserialize<SessionEvent>(data, s_json);
                if (evt is not null)
                    await channel.Writer.WriteAsync(evt, ct);
            }
            channel.Writer.TryComplete();
        }, ct);

        return channel.Reader;
    }
}

// SessionDetail.razor
@code {
    private List<SessionEvent> _events = new();
    private CancellationTokenSource _cts = new();

    protected override async Task OnInitializedAsync()
    {
        var reader = SseStream.SubscribeSession(SessionId, _cts.Token);
        await foreach (var evt in reader.ReadAllAsync(_cts.Token))
        {
            _events.Add(evt);
            await InvokeAsync(StateHasChanged);
        }
    }
}
```

### Pattern 3: MudBlazor Theme with Dark/Light Toggle
**What:** MudThemeProvider with custom dark palette, toggle via MudSwitch in appbar.
**When to use:** App-wide theming.
**Example:**
```csharp
// MainLayout.razor
<MudThemeProvider @ref="_themeProvider" Theme="_theme" IsDarkMode="_isDarkMode" />
<MudAppBar>
    <MudText Typo="Typo.h6">AgentHub Dashboard</MudText>
    <MudSpacer />
    <MudSwitch T="bool" @bind-Value="_isDarkMode" Color="Color.Inherit"
               ThumbIcon="@(_isDarkMode ? Icons.Material.Filled.DarkMode : Icons.Material.Filled.LightMode)" />
</MudAppBar>

@code {
    private bool _isDarkMode = true; // Dark by default
    private MudTheme _theme = new()
    {
        PaletteDark = new PaletteDark
        {
            Primary = Colors.Blue.Default,
            Surface = "#1e1e1e",
            Background = "#121212",
        }
    };
}
```

### Pattern 4: Aspire AppHost Wiring
**What:** Register both Service and Web in AppHost with service discovery reference.
**When to use:** AppHost Program.cs configuration.
**Example:**
```csharp
// AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);

var service = builder.AddProject<Projects.AgentHub_Service>("agenthub-service");

builder.AddProject<Projects.AgentHub_Web>("agenthub-web")
    .WithReference(service)
    .WithExternalHttpEndpoints();

builder.Build().Run();
```

### Anti-Patterns to Avoid
- **Blocking the SignalR circuit with long-running SSE reads:** Always use background tasks with Channel<T> or similar; never await SSE enumeration directly in OnInitializedAsync without InvokeAsync marshaling
- **Calling StateHasChanged from non-UI threads:** Always use `await InvokeAsync(StateHasChanged)` when updating state from background tasks
- **Creating HttpClient instances manually:** Use IHttpClientFactory for proper lifecycle management and Aspire service discovery
- **Storing all session output in component state unbounded:** Implement virtualization or buffer limits for sessions with large output volumes

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Data tables with sorting/filtering | Custom table component | MudDataGrid | Pagination, sorting, filtering, row selection all built-in |
| Modal dialogs | Custom overlay/popup | MudDialog / MudDialogService | Focus trap, overlay, animation, keyboard handling |
| Theme switching | CSS variables toggle | MudThemeProvider IsDarkMode | Automatic palette switching, cascading to all components |
| Layout scaffolding | Custom flex/grid layout | MudLayout + MudAppBar + MudDrawer | Responsive, consistent, handles sidebar state |
| Status indicators | Custom colored dots | MudChip / MudBadge with Color enum | Consistent Material Design status representation |
| Toast notifications | Custom alert system | MudSnackbar via ISnackbar | Queue management, auto-dismiss, positioning |
| Form validation | Manual validation logic | MudForm with DataAnnotations | Integrated validation display, field-level errors |
| SSE parsing | Manual string splitting | System.Net.ServerSentEvents SseParser | Handles edge cases (multi-line data, reconnection IDs, comments) |
| API client boilerplate | Raw HttpClient calls | Typed HttpClient class (mirror CLI pattern) | Consistent JSON options, error handling, query building |

**Key insight:** MudBlazor provides every UI component needed for this dashboard. The only custom rendering is the terminal output panel, which is intentionally custom for the VS Code terminal aesthetic.

## Common Pitfalls

### Pitfall 1: SignalR Circuit Timeout on Long SSE Streams
**What goes wrong:** Blazor Server components keep a SignalR connection to the browser. If a component's lifecycle method blocks waiting for SSE events, the circuit can appear unresponsive.
**Why it happens:** OnInitializedAsync runs on the circuit's sync context. A blocking await prevents other UI interactions.
**How to avoid:** Use a fire-and-forget background task to consume SSE, write to Channel<T>, read from channel in the component lifecycle with InvokeAsync(StateHasChanged).
**Warning signs:** Browser shows "Attempting to reconnect" overlay, UI freezes during streaming.

### Pitfall 2: StateHasChanged on Wrong Thread
**What goes wrong:** InvalidOperationException when calling StateHasChanged from a background thread.
**Why it happens:** Blazor Server requires UI updates to run on the renderer's synchronization context.
**How to avoid:** Always `await InvokeAsync(StateHasChanged)` when updating from background tasks or event handlers.
**Warning signs:** Runtime exceptions about synchronization context.

### Pitfall 3: Unbounded Memory Growth from Session Output
**What goes wrong:** A long-running session can produce thousands of output events. Storing all events in a List<> causes memory growth.
**Why it happens:** No natural backpressure on stored events in component state.
**How to avoid:** Implement a circular buffer or use MudBlazor's virtualization (MudVirtualize) for the output list. For completed sessions, load history pages on demand rather than all at once.
**Warning signs:** Browser tab memory growing steadily during streaming.

### Pitfall 4: MudBlazor Not Rendering in Blazor Server
**What goes wrong:** Components appear unstyled or don't render interactive features.
**Why it happens:** Missing MudBlazor service registration, CSS/JS includes, or MudThemeProvider in layout.
**How to avoid:** Follow the complete setup: `builder.Services.AddMudServices()` in Program.cs, add `<link>` and `<script>` tags in App.razor `<head>`/`<body>`, wrap content in `<MudThemeProvider>` + `<MudPopoverProvider>` + `<MudDialogProvider>` + `<MudSnackbarProvider>`.
**Warning signs:** Unstyled HTML elements, no ripple effects, dialogs not appearing.

### Pitfall 5: Aspire Service Discovery URL Format
**What goes wrong:** HttpClient base address doesn't resolve to the actual service URL.
**Why it happens:** Using wrong URI scheme format for Aspire service discovery.
**How to avoid:** Use `https+http://resource-name` format in HttpClient BaseAddress. The `https+http://` prefix tells Aspire to try HTTPS first, fall back to HTTP.
**Warning signs:** HttpRequestException with connection refused, name resolution failures.

### Pitfall 6: Disposal Leak on Navigation
**What goes wrong:** SSE streams keep running after navigating away from a page.
**Why it happens:** CancellationTokenSource not canceled on component disposal.
**How to avoid:** Implement IDisposable on components, cancel CTS in Dispose(). Use `@implements IDisposable`.
**Warning signs:** Multiple SSE connections open simultaneously, server resource exhaustion.

## Code Examples

### MudBlazor Setup in Program.cs
```csharp
// Source: MudBlazor official docs + Aspire patterns
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

// Typed HttpClient with Aspire service discovery
builder.Services.AddHttpClient<DashboardApiClient>(client =>
{
    client.BaseAddress = new Uri("https+http://agenthub-service");
});

builder.Services.AddHttpClient("SseClient", client =>
{
    client.BaseAddress = new Uri("https+http://agenthub-service");
    client.Timeout = Timeout.InfiniteTimeSpan; // SSE streams are long-lived
});

builder.Services.AddScoped<SseStreamService>();

var app = builder.Build();
app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.Run();
```

### Terminal Output Panel Component
```razor
@* TerminalOutput.razor *@
<div class="terminal-panel" @ref="_terminalRef">
    <div class="terminal-toolbar">
        <MudText Typo="Typo.caption">Output</MudText>
        <MudSpacer />
        <MudIconButton Icon="@(_autoScroll ? Icons.Material.Filled.Lock : Icons.Material.Filled.LockOpen)"
                       Size="Size.Small" OnClick="ToggleAutoScroll"
                       Title="@(_autoScroll ? "Auto-scroll ON" : "Auto-scroll OFF")" />
    </div>
    <div class="terminal-content" id="terminal-@SessionId">
        @foreach (var evt in Events)
        {
            <pre class="@GetEventClass(evt.Kind)">@evt.Data</pre>
        }
    </div>
</div>

@code {
    [Parameter] public List<SessionEvent> Events { get; set; } = new();
    [Parameter] public string SessionId { get; set; } = "";

    private bool _autoScroll = true;
    private ElementReference _terminalRef;

    private string GetEventClass(SessionEventKind kind) => kind switch
    {
        SessionEventKind.StdOut => "terminal-stdout",
        SessionEventKind.StdErr => "terminal-stderr",
        SessionEventKind.StateChanged => "terminal-state",
        _ => "terminal-info"
    };

    private void ToggleAutoScroll() => _autoScroll = !_autoScroll;
}
```

### Terminal CSS
```css
/* terminal.css */
.terminal-panel {
    background-color: #1e1e1e;
    border-radius: 4px;
    overflow: hidden;
    display: flex;
    flex-direction: column;
    height: 100%;
}
.terminal-content {
    flex: 1;
    overflow-y: auto;
    padding: 8px 12px;
    font-family: 'Cascadia Code', 'Consolas', 'Courier New', monospace;
    font-size: 13px;
    line-height: 1.4;
}
.terminal-content pre {
    margin: 0;
    padding: 1px 0;
    white-space: pre-wrap;
    word-break: break-all;
}
.terminal-stdout { color: #d4d4d4; }
.terminal-stderr { color: #f44747; }
.terminal-state { color: #dcdcaa; }
.terminal-info { color: #569cd6; }
```

### Session Launch Dialog
```razor
@* LaunchDialog.razor *@
<MudDialog>
    <TitleContent>Launch Session</TitleContent>
    <DialogContent>
        <MudSelect T="string" @bind-Value="_agentType" Label="Agent Type" Required>
            @foreach (var agent in _agents)
            {
                <MudSelectItem Value="@agent">@agent</MudSelectItem>
            }
        </MudSelect>
        <MudSelect T="string" @bind-Value="_hostId" Label="Host" Required>
            <MudSelectItem Value="@("")">Auto-place</MudSelectItem>
            @foreach (var host in _hosts)
            {
                <MudSelectItem Value="@host.HostId">@host.DisplayName</MudSelectItem>
            }
        </MudSelect>
        <MudTextField T="string" @bind-Value="_prompt" Label="Prompt" Lines="3" />
        <MudCheckBox T="bool" @bind-Value="_fireAndForget" Label="Fire and forget" />
        <MudCheckBox T="bool" @bind-Value="_skipPermissions" Label="Skip permissions" />
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="Cancel">Cancel</MudButton>
        <MudButton Color="Color.Primary" OnClick="Submit">Launch</MudButton>
    </DialogActions>
</MudDialog>
```

### Polling + SSE Toggle Pattern
```csharp
// FleetOverview.razor @code block
private Timer? _pollingTimer;
private bool _sseEnabled;
private CancellationTokenSource? _sseCts;

protected override async Task OnInitializedAsync()
{
    await LoadData();
    StartPolling();
}

private void StartPolling()
{
    _pollingTimer = new Timer(async _ =>
    {
        if (!_sseEnabled)
        {
            await LoadData();
            await InvokeAsync(StateHasChanged);
        }
    }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
}

private async Task ToggleSse()
{
    _sseEnabled = !_sseEnabled;
    if (_sseEnabled)
    {
        _sseCts = new CancellationTokenSource();
        _ = ConsumeFleetEvents(_sseCts.Token);
    }
    else
    {
        _sseCts?.Cancel();
    }
}

private async Task ConsumeFleetEvents(CancellationToken ct)
{
    var reader = SseStream.SubscribeFleet(ct);
    await foreach (var evt in reader.ReadAllAsync(ct))
    {
        UpdateSessionFromEvent(evt);
        await InvokeAsync(StateHasChanged);
    }
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Blazor Server (classic) | Blazor Web App with render modes | .NET 8+ | Project uses Blazor Server specifically (user decision) -- simpler for this use case |
| Manual SSE parsing (string split) | System.Net.ServerSentEvents SseParser | .NET 9 | Already used in CLI -- reuse for Blazor |
| Aspire preview packages | Aspire stable (13.x) | 2025-2026 | Current project has 9.0.0-preview.1 -- should work, but upgrading is an option |
| MudBlazor 6.x/7.x | MudBlazor 9.1.0 | March 2026 | Full .NET 10 support, improved DataGrid |

**Deprecated/outdated:**
- Blazor Server template (`blazorserver`): replaced by unified `blazor` template with `--interactivity Server` flag in .NET 8+
- MudTable for complex grids: MudDataGrid is the preferred component for sortable/filterable data tables

## Open Questions

1. **Aspire AppHost package version**
   - What we know: Current project has `Aspire.Hosting.AppHost` 9.0.0-preview.1. Latest stable is 13.1.2.
   - What's unclear: Whether upgrading is necessary or if the preview version supports AddProject<> with service discovery.
   - Recommendation: Keep current version if it works. The AddProject + WithReference pattern has been stable since Aspire 8.x. Upgrade only if compilation issues arise.

2. **Auto-scroll JavaScript Interop**
   - What we know: Blazor can use JS interop to scroll elements. MudBlazor has some scroll utilities.
   - What's unclear: Whether CSS `overflow-anchor: auto` is sufficient or if JS interop is needed for reliable auto-scroll with scroll-lock toggle.
   - Recommendation: Start with CSS `overflow-anchor: auto` on the terminal container. Add JS interop (`scrollIntoView`) only if CSS-only approach is unreliable.

3. **MudBlazor Blazor Web App Template Compatibility**
   - What we know: MudBlazor issue tracker notes that the "Blazor Web App" template (with render modes) has some compatibility concerns. Classic Blazor Server works fully.
   - What's unclear: Whether `dotnet new blazor --interactivity Server` creates a "Blazor Web App" that might have edge cases vs the classic `blazorserver` template.
   - Recommendation: Use the `blazor` template with `--interactivity Server` as it is the current standard. MudBlazor 9.1.0 supports it. If issues arise, the classic template is a safe fallback.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xunit 2.9.x + Microsoft.NET.Test.Sdk 17.x |
| Config file | tests/AgentHub.Tests/AgentHub.Tests.csproj |
| Quick run command | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~Web" -x` |
| Full suite command | `dotnet test tests/AgentHub.Tests` |

### Phase Requirements -> Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| WEB-01 | Fleet overview page lists hosts and session status | integration | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~DashboardApiClientTests" -x` | No - Wave 0 |
| WEB-01 | Live updates via polling | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~DashboardApiClientTests" -x` | No - Wave 0 |
| WEB-02 | Real-time output streaming via SSE | integration | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SseStreamServiceTests" -x` | No - Wave 0 |
| WEB-02 | Session history replay with pagination | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~DashboardApiClientTests" -x` | No - Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~Web OR FullyQualifiedName~Dashboard OR FullyQualifiedName~SseStream" -x`
- **Per wave merge:** `dotnet test tests/AgentHub.Tests`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `tests/AgentHub.Tests/DashboardApiClientTests.cs` -- covers WEB-01 (API client methods for sessions, hosts)
- [ ] `tests/AgentHub.Tests/SseStreamServiceTests.cs` -- covers WEB-02 (SSE consumption + Channel bridge)
- [ ] Add `ProjectReference` to `AgentHub.Web` in test project csproj

**Note:** Blazor component rendering tests (bUnit) are out of scope for v1. Focus testing on the service/API client layer which is where the business logic lives. The UI components are primarily MudBlazor wrappers with minimal custom logic.

## Sources

### Primary (HIGH confidence)
- [NuGet: MudBlazor 9.1.0](https://www.nuget.org/packages/MudBlazor) - Version, .NET 10 support confirmed
- [NuGet: Aspire.Hosting.AppHost](https://www.nuget.org/packages/Aspire.Hosting.AppHost) - Latest version 13.1.2
- Existing codebase: AgentHub.Cli/Api/AgentHubApiClient.cs, SseStreamReader.cs -- proven patterns for API consumption and SSE streaming
- Existing codebase: AgentHub.Contracts/Models.cs -- all DTOs already defined
- Existing codebase: AgentHub.Service/Program.cs -- all API endpoints confirmed

### Secondary (MEDIUM confidence)
- [MudBlazor .NET 10 Issue #12049](https://github.com/MudBlazor/MudBlazor/issues/12049) - .NET 10 target support tracking
- [Aspire Service Discovery Docs](https://learn.microsoft.com/en-us/dotnet/aspire/service-discovery/overview) - Service discovery URI format
- [Adding Aspire to Existing Apps](https://devblogs.microsoft.com/dotnet/adding-dotnet-aspire-to-your-existing-dotnet-apps/) - AppHost patterns
- [SSE in .NET 10](https://dev.to/mashrulhaque/server-sent-events-in-net-10-finally-a-native-solution-22kg) - Native SSE support patterns
- [Blazor StateHasChanged Threading](https://github.com/dotnet/aspnetcore/issues/22286) - InvokeAsync pattern requirement

### Tertiary (LOW confidence)
- [MudBlazor Blazor Web App compatibility](https://github.com/MudBlazor/MudBlazor/issues/12206) - Some reported issues with browser refresh in VS2026, may not affect Blazor Server specifically

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - MudBlazor 9.1.0 confirmed .NET 10 compatible, Aspire patterns well-documented, SSE already proven in CLI
- Architecture: HIGH - Blazor Server + typed HttpClient + Channel<T> bridge is well-established pattern, directly mirrors existing CLI architecture
- Pitfalls: HIGH - SignalR circuit, StateHasChanged threading, disposal are well-documented Blazor Server concerns

**Research date:** 2026-03-08
**Valid until:** 2026-04-07 (30 days -- stable ecosystem)
