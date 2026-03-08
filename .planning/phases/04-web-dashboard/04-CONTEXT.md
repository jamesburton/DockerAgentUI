# Phase 4: Web Dashboard - Context

**Gathered:** 2026-03-08
**Status:** Ready for planning

<domain>
## Phase Boundary

A Blazor web dashboard provides visual fleet oversight with live session status and real-time output streaming. Users can view all hosts and sessions, click into a session to see real-time agent output, launch new sessions, stop running sessions, and resolve approval requests — all from the browser. Dashboard updates live via SSE.

</domain>

<decisions>
## Implementation Decisions

### Blazor Hosting Model
- Blazor Server (SignalR-based) — best for real-time SSE consumption, simpler deployment
- Separate project: AgentHub.Web, references AgentHub.Contracts, calls AgentHub.Service via HTTP/SSE
- Wire into Aspire AppHost for service discovery (AppHost is currently a stub — this gives it purpose)
- MudBlazor component library for Material Design UI components (data tables, cards, dialogs, theme support)

### Fleet Overview Layout
- Split panel design: host sidebar on the left, session data table on the right
- Host sidebar: compact status cards showing host name, status dot (online/offline), session count badge, CPU/memory mini-bars. Click to filter session list. "All hosts" option for fleet-wide view
- Session panel: MudBlazor DataTable with columns — status, agent type, prompt (truncated), host, duration, started time. Sortable and filterable. Row click navigates to session detail page
- Real-time updates: periodic polling (5-10s) by default. "Enable All-Session Updates" toggle switches to full SSE-driven instant updates via fleet-wide /api/events endpoint

### Session Detail View
- Dedicated /session/{id} page (bookmarkable URL routing, back button returns to fleet view)
- Top section: session metadata header (status, agent type, host, duration, prompt, exit code)
- Below: scrolling terminal-style output panel with dark background, monospace font
- Terminal aesthetic: stdout in white/green, stderr in red, state changes in yellow. Auto-scroll with scroll-lock button to pause
- Full history replay for completed sessions via /api/sessions/{id}/history endpoint with paginated loading

### Dashboard Scope & Interactivity
- Full CRUD: monitor sessions, launch new ones, stop running sessions
- Session launch: simple MudBlazor dialog/modal with agent type dropdown, host selector (or auto-place), prompt text area, optional flags (fire-and-forget, skip-permissions)
- Stop sessions: available from session table and detail page
- Inline approval handling: approval requests appear as alerts/dialogs, user can approve/reject directly from the web using existing /api/approvals/{id}/resolve endpoint

### Theme
- Dark mode by default (matches terminal aesthetic for output panel)
- Light mode toggle available in toolbar
- MudBlazor built-in theme support

### Claude's Discretion
- Exact MudBlazor component choices and layout breakpoints
- SSE client implementation for Blazor Server (SignalR circuit vs HttpClient-based)
- Polling interval and SSE toggle persistence (localStorage vs server-side)
- Terminal output rendering approach (pre-formatted text vs virtual scrolling)
- Aspire AppHost resource configuration details
- Navigation structure (sidebar nav vs top nav)
- Error handling and loading states

</decisions>

<specifics>
## Specific Ideas

- The terminal output panel should feel like VS Code's integrated terminal — dark background, monospace, color-coded
- The split panel fleet overview is the primary landing page — user sees hosts on the left and sessions on the right immediately
- "Enable All-Session Updates" toggle lets power users opt into full SSE streaming for instant fleet updates vs the default polling approach
- Session launch dialog should be quick and minimal — operator often knows exactly what they want to launch
- Approval handling mirrors CLI behavior: approval requests appear contextually, user resolves them, session unblocks

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- **AgentHub.Cli/Api/AgentHubApiClient.cs**: Full HTTP client wrapper with GetSessionsAsync, GetHostsAsync, StartSessionAsync, StopSessionAsync, ResolveApprovalAsync — pattern directly reusable for Blazor service layer
- **AgentHub.Cli/Api/SseStreamReader.cs**: SSE consumer with automatic reconnection and exponential backoff — logic reusable for Blazor SSE consumption
- **AgentHub.Contracts/Models.cs**: All DTOs (SessionSummary, SessionEvent, HostRecord, StartSessionRequest, SessionEventKind) shared across projects
- **AgentHub.AppHost**: Stub Aspire AppHost ready to wire in AgentHub.Service + new AgentHub.Web

### Established Patterns
- REST API endpoints fully built: sessions CRUD, hosts, skills, policy, approvals, events
- SSE endpoints: per-session (/api/sessions/{id}/events) and fleet-wide (/api/events)
- Approval resolution: POST /api/approvals/{id}/resolve with ApprovalResolveRequest
- Session history: GET /api/sessions/{id}/history with pagination (page, pageSize, kind filter)
- JSON serialization: JsonSerializerDefaults.Web (camelCase) throughout

### Integration Points
- AgentHub.Web connects to AgentHub.Service via HTTP REST + SSE (same pattern as CLI)
- Aspire AppHost provides service discovery for the API URL
- All session operations go through ISessionCoordinator on server side
- Events streamed via DurableEventService + SseSubscriptionManager

</code_context>

<deferred>
## Deferred Ideas

- OS-level toast notifications from the web dashboard — evaluate when MAUI client arrives
- Split-pane multi-session watch (view multiple session outputs simultaneously) — release candidate feature
- Dashboard-based config editing (skills, policies, agent definitions) — future iteration
- Session diff review workflow (view agent code changes, approve/reject before merge) — v2 requirement (MON-05)

</deferred>

---

*Phase: 04-web-dashboard*
*Context gathered: 2026-03-08*
