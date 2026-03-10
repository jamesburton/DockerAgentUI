# Phase 8: Interactive Session Steering - Context

**Gathered:** 2026-03-10
**Status:** Ready for planning

<domain>
## Phase Boundary

Send follow-up instructions to a running agent session from CLI or Blazor UI, with visual distinction from the original prompt and delivery confirmation from the host daemon. Pause/resume and idle detection are deferred to v1.2 (INTER-10, INTER-11, INTER-12).

</domain>

<decisions>
## Implementation Decisions

### Visual distinction
- Both the original prompt AND follow-up instructions render visually distinct from agent output (all operator messages are "input" events)
- Follow-up instructions visible in both per-session and fleet-wide SSE streams
- Claude's discretion on exact CLI styling (colored prefix vs bordered panel) and Web styling (chat-bubble vs tagged inline) — pick what fits existing Spectre.Console Live and MudBlazor patterns

### Delivery confirmation UX
- Both: flash indicator near input area for immediate feedback + durable event entry in the event stream for audit trail
- Timeout with warning: if host daemon doesn't acknowledge within a few seconds, show "⚠ Delivery unconfirmed" warning in the event stream
- Claude's discretion on acknowledgment level (daemon received vs agent stdin written) and whether confirmation includes the steering text or just a reference marker

### Steering architecture
- Steering goes through the same policy/sanitization/approval pipeline as regular input — consistent security model
- Each steering command emits a SessionEvent with a new kind (e.g., SteeringInput) — queryable through existing session history API with kind filter
- Any operator can steer any session (single-operator system, supports team handoffs)
- Claude's discretion on whether to enhance existing SendInput endpoint with metadata or create a new endpoint — pick the cleanest architecture given SendInputRequest and host daemon protocol

### Error & edge cases
- UI input always available regardless of session state — server validates and returns appropriate errors (prevents jarring UX when state changes mid-typing)
- Soft warning on rapid-fire: after 3+ commands in quick succession, warn "Sending multiple commands rapidly — agent may not process them in order"
- Claude's discretion on SSH failure handling (fail fast vs auto-retry) and not-running session error messages

### Claude's Discretion
- Exact CLI visual treatment (Spectre.Console component choices for steering display)
- Exact Web visual treatment (MudBlazor component choices for steering display)
- Delivery acknowledgment depth (daemon-level vs stdin-level)
- Endpoint architecture (enhance SendInput vs new /steer endpoint)
- SSH failure retry behavior
- Not-running session error message detail level
- Delivery confirmation timeout duration
- Rapid-fire warning threshold and cooldown

</decisions>

<specifics>
## Specific Ideas

- Original prompt should render the same way as follow-ups — all operator messages are visually "input" events, distinct from agent output
- Fleet-wide visibility of follow-ups: operators monitoring multiple sessions see all steering activity across the fleet
- Delivery confirmation is two-tier: immediate flash for responsiveness + event log for auditability
- Timeout warning prevents silent failures — operator always knows if their command might not have landed

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- **SendInputRequest** (Contracts/Models.cs): DTO with input, skillId, arguments, requiresElevation fields — extensible for steering metadata
- **POST /api/sessions/{id}/input** (Program.cs): Server endpoint fully implemented, calls coordinator.SendInputAsync
- **SessionCoordinator.SendInputAsync** (SessionCoordinator.cs): Full pipeline with policy check → sanitization → approval gating → backend
- **SshBackend.SendInputAsync** (SshBackend.cs): SSH stdin write, JSON protocol over SSH
- **HostDaemonModels.cs**: Extensible host command protocol with well-known commands and pluggable payloads
- **SessionEventKind enum** (Contracts/Models.cs): Extensible — add SteeringInput kind
- **SessionEvent.Meta** (Dictionary<string,string>): Carries contextual data, serialized as JSON in DB
- **DurableEventService** (Events/): Persists events to DB + broadcasts to SSE subscribers
- **AgentHubApiClient.SendInputAsync** (CLI): HTTP client method for sending input
- **DashboardApiClient.SendInputAsync** (Web): HTTP client method for sending input
- **SessionWatchCommand** (CLI): Live display with 'i' hotkey input mode — extend for steering display
- **SessionDetail.razor** (Web): Fixed bottom bar with TextField + Send button — extend for steering display
- **ApprovalService** (Coordinator/): Request/response tracking pattern with timeout — reusable for delivery confirmation

### Established Patterns
- Background services use IDbContextFactory for scoped DB access
- Events emitted via Func<SessionEvent, Task> callback through SessionCoordinator
- Host daemon protocol: single-line JSON over SSH stdin (commands) / stdout (responses)
- CLI uses Spectre.Console Live display with hotkey detection
- Web uses MudBlazor dark theme with MudSnackbar for notifications
- SSE events flow: DurableEventService → SseSubscriptionManager → per-session + fleet-wide channels
- Session history queryable via GET /api/sessions/{id}/history with kind filter

### Integration Points
- **SessionEventKind**: Add SteeringInput (and possibly SteeringDelivered) enum values
- **SendInputRequest or new SteerRequest**: Add steering metadata (isFollowUp, sequenceNumber)
- **HostDaemonModels.cs**: May need new command type or acknowledgment payload
- **SessionWatchCommand.cs**: Render steering events distinctly in Live display
- **SessionDetail.razor**: Render steering events distinctly in terminal output
- **FleetOverview.razor**: Show steering events in fleet-wide stream
- **DurableEventService**: Emit steering and delivery confirmation events

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 08-interactive-session-steering*
*Context gathered: 2026-03-10*
