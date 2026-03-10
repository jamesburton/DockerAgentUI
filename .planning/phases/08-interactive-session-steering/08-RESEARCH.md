# Phase 8: Interactive Session Steering - Research

**Researched:** 2026-03-10
**Domain:** Real-time session input with delivery confirmation across CLI, Web, and SSH backend
**Confidence:** HIGH

## Summary

Phase 8 adds follow-up steering commands to running agent sessions. The existing codebase already has a complete SendInput pipeline (API endpoint, coordinator with policy/sanitization/approval, SSH backend write, CLI hotkey input, Web TextField). The work is primarily about: (1) adding a new `SteeringInput` event kind to distinguish follow-ups from initial prompts, (2) implementing delivery confirmation with a timeout-based acknowledgment pattern, (3) updating both CLI and Web UIs to render steering events distinctly, and (4) adding rapid-fire warnings.

The architecture is well-established. The `SendInputRequest` DTO, `SessionCoordinator.SendInputAsync`, `SshBackend.SendInputAsync`, `DurableEventService.EmitAsync`, and both API clients already handle the full flow. The HostDaemon protocol supports extensible commands with JSON payloads over SSH stdin/stdout. The `ApprovalService` pattern (ConcurrentDictionary of TaskCompletionSource with timeout) is directly reusable for delivery confirmation tracking.

**Primary recommendation:** Extend the existing SendInput endpoint with optional steering metadata rather than creating a new endpoint. Add `SteeringInput` and `SteeringDelivered` to `SessionEventKind`. Use the ApprovalService's timeout+TCS pattern for delivery confirmation.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- Both the original prompt AND follow-up instructions render visually distinct from agent output (all operator messages are "input" events)
- Follow-up instructions visible in both per-session and fleet-wide SSE streams
- Both: flash indicator near input area for immediate feedback + durable event entry in the event stream for audit trail
- Timeout with warning: if host daemon doesn't acknowledge within a few seconds, show "Warning: Delivery unconfirmed" warning in the event stream
- Steering goes through the same policy/sanitization/approval pipeline as regular input -- consistent security model
- Each steering command emits a SessionEvent with a new kind (e.g., SteeringInput) -- queryable through existing session history API with kind filter
- Any operator can steer any session (single-operator system, supports team handoffs)
- UI input always available regardless of session state -- server validates and returns appropriate errors (prevents jarring UX when state changes mid-typing)
- Soft warning on rapid-fire: after 3+ commands in quick succession, warn "Sending multiple commands rapidly -- agent may not process them in order"

### Claude's Discretion
- Exact CLI visual treatment (Spectre.Console component choices for steering display)
- Exact Web visual treatment (MudBlazor component choices for steering display)
- Delivery acknowledgment depth (daemon-level vs stdin-level)
- Endpoint architecture (enhance SendInput vs new /steer endpoint)
- SSH failure retry behavior
- Not-running session error message detail level
- Delivery confirmation timeout duration
- Rapid-fire warning threshold and cooldown

### Deferred Ideas (OUT OF SCOPE)
None -- discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| INTER-01 | User can send follow-up instructions to a running agent session mid-task | Existing SendInput pipeline handles full flow; add `IsFollowUp` metadata flag to distinguish from initial input; emit SteeringInput event kind |
| INTER-02 | CLI and Blazor UI visually distinguish initial prompt from follow-up steering | CLI: Spectre.Console colored prefix in FormatEvent; Web: CSS class in TerminalOutput.razor GetEventClass + styled pre tag; FleetOverview SSE handler reacts to SteeringInput kind |
| INTER-03 | Coordinator receives acknowledgment from host daemon confirming command delivery | Add `send-input` command to HostCommandProtocol; host daemon returns success/failure response; coordinator tracks delivery via TCS+timeout pattern (reuse ApprovalService design) |
</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| .NET (existing) | 10.0 | Runtime | Already in use |
| Spectre.Console | (existing) | CLI Live display, markup | Already used in SessionWatchCommand |
| MudBlazor | (existing) | Web UI components | Already used in SessionDetail.razor |
| SSH.NET | (existing) | SSH host communication | Already used in SshBackend |
| System.Text.Json | (existing) | JSON serialization | Used throughout HostDaemon protocol |
| xUnit | 2.9.x | Testing | Existing test framework |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| EF Core InMemory | 10.0.2 | Test DB | Already used in test infrastructure |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.2 | Integration tests | Already used for API endpoint tests |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Extending SendInput endpoint | New /steer endpoint | Separate endpoint adds surface area but no benefit -- same pipeline, same backend call. Recommend extending with metadata. |
| TCS+timeout for delivery confirmation | Fire-and-forget with polling | TCS pattern already proven in ApprovalService, gives clean async/await semantics |
| New HostCommand for steering | Reuse existing JSON stdin write | New command type enables daemon-level acknowledgment; raw stdin write has no response channel |

## Architecture Patterns

### Recommended Changes by Layer

```
AgentHub.Contracts/Models.cs
  - Add SessionEventKind.SteeringInput
  - Add SessionEventKind.SteeringDelivered
  - Extend SendInputRequest with IsFollowUp bool

AgentHub.Orchestration/
  HostDaemon/HostDaemonModels.cs
    - Add HostCommand.SendInput constant ("send-input")
    - Add SendInputPayload record
  HostDaemon/HostCommandProtocol.cs
    - Add CreateSendInput() factory method
  Coordinator/SessionCoordinator.cs
    - Emit SteeringInput event before backend call
    - After backend call, await delivery confirmation with timeout
    - Emit SteeringDelivered or warning event based on result
  Backends/SshBackend.cs
    - Change SendInputAsync to use HostCommandProtocol (JSON command with response)
    - Return acknowledgment success/failure from daemon response

AgentHub.Service/Program.cs
  - Extend POST /api/sessions/{id}/input response to include delivery status

AgentHub.Cli/
  Commands/Session/SessionWatchCommand.cs
    - Add SteeringInput case to FormatEvent (distinct color/prefix)
    - Add SteeringDelivered case
    - Add rapid-fire warning logic
  Api/AgentHubApiClient.cs
    - Update SendInputAsync to accept IsFollowUp parameter
    - Parse delivery confirmation response

AgentHub.Web/
  Components/Shared/TerminalOutput.razor
    - Add terminal-steering CSS class
    - Add GetEventClass case for SteeringInput
  Components/Pages/SessionDetail.razor
    - Flash indicator on send (MudSnackbar or inline animation)
    - Track rapid-fire count, show warning
  Services/DashboardApiClient.cs
    - Update SendInputAsync to pass IsFollowUp flag
```

### Pattern 1: Delivery Confirmation via TCS+Timeout
**What:** Coordinator sends steering command to backend, starts a TaskCompletionSource with configurable timeout (5 seconds recommended). Backend returns HostCommandResponse. If response arrives before timeout, emit SteeringDelivered event. If timeout, emit warning event with "Delivery unconfirmed" message.
**When to use:** Every steering command.
**Example:**
```csharp
// In SessionCoordinator.SendInputAsync (after backend call):
var deliveryTcs = new TaskCompletionSource<bool>();
var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5), ct);

// SshBackend.SendInputAsync now returns bool (success from daemon response)
var delivered = await backend.SendInputAsync(sessionId, normalizedRequest, ct);

if (delivered)
{
    await emit(new SessionEvent(sessionId, SessionEventKind.SteeringDelivered,
        DateTimeOffset.UtcNow, "Steering command delivered",
        new Dictionary<string, string> { ["input"] = request.Input }));
}
else
{
    await emit(new SessionEvent(sessionId, SessionEventKind.Info,
        DateTimeOffset.UtcNow, "Warning: Delivery unconfirmed -- host daemon did not acknowledge",
        new Dictionary<string, string> { ["warning"] = "delivery-unconfirmed" }));
}
```

### Pattern 2: Rapid-Fire Detection (Client-Side)
**What:** Track timestamps of recent steering commands. After 3+ within a sliding window (e.g., 10 seconds), show a warning. This is client-side only -- no server enforcement.
**When to use:** Both CLI and Web clients.
**Example:**
```csharp
// CLI: in SessionWatchCommand input handler
private static readonly Queue<DateTimeOffset> _recentInputs = new();
private const int RapidFireThreshold = 3;
private static readonly TimeSpan RapidFireWindow = TimeSpan.FromSeconds(10);

private static bool CheckRapidFire()
{
    var now = DateTimeOffset.UtcNow;
    _recentInputs.Enqueue(now);
    while (_recentInputs.Count > 0 && now - _recentInputs.Peek() > RapidFireWindow)
        _recentInputs.Dequeue();
    return _recentInputs.Count >= RapidFireThreshold;
}
```

### Pattern 3: Steering Event Rendering
**What:** SteeringInput events rendered with distinct visual treatment -- cyan/magenta prefix in CLI, left-border highlight in Web.
**CLI recommendation:** `[cyan]{ts} STEER>[/] {data}` prefix in FormatEvent
**Web recommendation:** CSS class `terminal-steering` with left border color `#4fc3f7` (material light-blue) and background `rgba(79, 195, 247, 0.05)`

### Anti-Patterns to Avoid
- **Creating a separate /steer endpoint:** Adds unnecessary API surface. The existing /input endpoint with an IsFollowUp flag is cleaner and reuses all policy/sanitization.
- **Server-side rapid-fire blocking:** The user decision says "soft warning" not "hard block." Never refuse to send -- just warn.
- **Polling for delivery confirmation:** The SSH protocol is request/response -- use the response directly rather than polling.
- **Removing input field when session stops:** User decision says input always available. Server validates state.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Delivery confirmation tracking | Custom tracking dictionary | Extend SshBackend.SendInputAsync to return bool from HostCommandResponse.Success | Host daemon protocol already has request/response semantics |
| Event persistence + SSE broadcast | Manual DB write + channel push | DurableEventService.EmitAsync | Already handles persist-then-broadcast atomically |
| SSH JSON protocol commands | Raw string concatenation | HostCommandProtocol factory methods + Serialize | Consistent serialization, null handling |
| Async timeout pattern | Manual Task.WhenAny + cancellation | CancellationTokenSource.CreateLinkedTokenSource with timeout | Clean cancellation propagation |

## Common Pitfalls

### Pitfall 1: Spectre.Console Live Display Input Blocking
**What goes wrong:** Reading Console.ReadLine() inside an AnsiConsole.Live() block corrupts output.
**Why it happens:** Spectre.Console Live takes exclusive control of stdout.
**How to avoid:** Exit the Live context before prompting (already implemented -- see SessionWatchCommand lines 147-158 where it exits Live, reads input, then re-enters). Follow this exact pattern for steering.
**Warning signs:** Garbled terminal output when typing.

### Pitfall 2: SSH Connection Lost During Steering
**What goes wrong:** SshBackend.SendInputAsync throws if the connection dropped between state check and write.
**Why it happens:** SSH connections can drop silently.
**How to avoid:** Catch exception in SendInputAsync, emit a delivery failure event, return appropriate error to client. Don't retry automatically (fail fast per user's discretion area).
**Warning signs:** Unhandled exception in coordinator pipeline.

### Pitfall 3: Race Between Session Stop and Steering
**What goes wrong:** User types steering command while session is stopping; backend throws because connection was cleaned up.
**Why it happens:** Session state transitions are asynchronous; UI may not reflect current state.
**How to avoid:** User decision says input always available -- catch errors server-side, return appropriate HTTP status (409 Conflict or 400 Bad Request with clear message). Never disable the input field based on state.
**Warning signs:** 500 errors when sending to recently-stopped sessions.

### Pitfall 4: SendInputRequest Record Immutability
**What goes wrong:** Adding IsFollowUp to SendInputRequest changes its constructor signature, breaking all existing callers.
**Why it happens:** C# records have positional constructor parameters.
**How to avoid:** Add IsFollowUp with a default value: `bool IsFollowUp = false`. This preserves backward compatibility with existing `new SendInputRequest(text)` calls.
**Warning signs:** Compilation errors in CLI and Web API client code.

### Pitfall 5: Event Kind Enum Ordering
**What goes wrong:** Adding enum values in the middle changes integer values of existing kinds.
**Why it happens:** C# enums assign sequential integers by default.
**How to avoid:** Always append new values at the end of SessionEventKind. SteeringInput and SteeringDelivered go after HostMetrics.
**Warning signs:** Existing events in DB suddenly have wrong kind labels.

## Code Examples

### Extending SendInputRequest (backward-compatible)
```csharp
// Source: AgentHub.Contracts/Models.cs
public sealed record SendInputRequest(
    string Input,
    bool IsBinary = false,
    string? SkillId = null,
    Dictionary<string, string>? Arguments = null,
    bool RequiresElevation = false,
    bool IsFollowUp = false);  // NEW - defaults to false for backward compat
```

### New SessionEventKind Values
```csharp
// Source: AgentHub.Contracts/Models.cs - append at end
public enum SessionEventKind
{
    // ... existing values ...
    HostMetrics,
    SteeringInput,      // NEW
    SteeringDelivered   // NEW
}
```

### New HostCommand Constant and Protocol
```csharp
// Source: AgentHub.Orchestration/HostDaemon/HostDaemonModels.cs
public sealed record HostCommand
{
    // ... existing constants ...
    public const string SendInput = "send-input";  // NEW
}

public sealed record SendInputPayload  // NEW
{
    [JsonPropertyName("input")]
    public string Input { get; init; } = "";

    [JsonPropertyName("isFollowUp")]
    public bool IsFollowUp { get; init; }
}
```

### HostCommandProtocol Factory Method
```csharp
// Source: AgentHub.Orchestration/HostDaemon/HostCommandProtocol.cs
public static HostCommand CreateSendInput(string sessionId, string input, bool isFollowUp = false)
{
    var payload = new SendInputPayload { Input = input, IsFollowUp = isFollowUp };
    return new HostCommand
    {
        Command = HostCommand.SendInput,
        SessionId = sessionId,
        Payload = JsonSerializer.SerializeToElement(payload, s_options)
    };
}
```

### Updated SshBackend.SendInputAsync (with acknowledgment)
```csharp
// Source: AgentHub.Orchestration/Backends/SshBackend.cs
public async Task<bool> SendInputAsync(string sessionId, SendInputRequest request, CancellationToken ct)
{
    if (!_connections.TryGetValue(sessionId, out var connection))
        throw new InvalidOperationException($"No active SSH connection for session {sessionId}.");

    if (!connection.IsConnected)
        throw new InvalidOperationException($"SSH connection for session {sessionId} is disconnected.");

    var command = HostCommandProtocol.CreateSendInput(sessionId, request.Input, request.IsFollowUp);
    var commandJson = HostCommandProtocol.Serialize(command);
    var responseJson = await connection.ExecuteCommandAsync(commandJson, ct);
    var response = HostCommandProtocol.DeserializeResponse(responseJson);
    return response.Success;
}
```

### CLI FormatEvent Extension
```csharp
// Source: AgentHub.Cli/Commands/Session/SessionWatchCommand.cs
SessionEventKind.SteeringInput => $"[cyan]{ts} STEER>[/] ",
SessionEventKind.SteeringDelivered => $"[green]{ts} DELIVERED[/] ",
```

### Web TerminalOutput CSS Class
```csharp
// Source: AgentHub.Web/Components/Shared/TerminalOutput.razor
SessionEventKind.SteeringInput => "terminal-steering",
SessionEventKind.SteeringDelivered => "terminal-info",
```

### API Endpoint Response Enhancement
```csharp
// Source: AgentHub.Service/Program.cs
app.MapPost("/api/sessions/{sessionId}/input", async (...) =>
{
    var result = await coordinator.SendInputAsync(user.UserId, sessionId, req, events.EmitAsync, ct);
    return Results.Ok(new { delivered = result.Delivered, warning = result.Warning });
});
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Raw stdin JSON write (no ack) | HostCommandProtocol with response | This phase | Enables delivery confirmation |
| Single input mode (no distinction) | SteeringInput event kind | This phase | Enables visual distinction and queryable history |
| No delivery feedback | Two-tier: flash + event | This phase | Operators always know if command landed |

## Open Questions

1. **ISessionBackend.SendInputAsync return type change**
   - What we know: Currently returns `Task` (void). Needs to return delivery status.
   - What's unclear: Whether to change interface to `Task<bool>` (breaking all backends) or add a separate method.
   - Recommendation: Change to `Task<bool>` -- only 3 backends (Ssh, Nomad, InMemory), all in-tree. InMemory/Nomad can return `true` immediately. Low risk.

2. **Coordinator.SendInputAsync return type**
   - What we know: Currently returns `Task` (void). API endpoint currently returns `Results.Accepted()`.
   - What's unclear: Whether to return a result DTO or keep emitting events and let client observe via SSE.
   - Recommendation: Return a `SteeringResult` record from coordinator. Endpoint returns it as JSON. Client gets immediate confirmation AND SSE event for audit.

3. **Initial prompt rendering as SteeringInput**
   - What we know: User decision says original prompt should render the same way as follow-ups.
   - What's unclear: Whether to retroactively emit a SteeringInput event for the initial prompt at session start.
   - Recommendation: Yes -- emit a SteeringInput event with `Meta["isInitial"] = "true"` during StartSessionAsync. This makes all operator inputs appear uniformly in the event stream.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.x |
| Config file | tests/AgentHub.Tests/AgentHub.Tests.csproj |
| Quick run command | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~Steering" -x` |
| Full suite command | `dotnet test tests/AgentHub.Tests` |

### Phase Requirements -> Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| INTER-01 | SendInputAsync with IsFollowUp emits SteeringInput event | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SteeringInput" --no-build` | No - Wave 0 |
| INTER-01 | API endpoint accepts IsFollowUp flag | integration | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SteeringEndpoint" --no-build` | No - Wave 0 |
| INTER-02 | FormatEvent renders SteeringInput with STEER prefix | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SteeringFormat" --no-build` | No - Wave 0 |
| INTER-02 | TerminalOutput maps SteeringInput to terminal-steering class | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SteeringCss" --no-build` | No - Wave 0 |
| INTER-03 | SshBackend returns delivery status from daemon response | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SteeringDelivery" --no-build` | No - Wave 0 |
| INTER-03 | Coordinator emits SteeringDelivered event on success | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SteeringDelivered" --no-build` | No - Wave 0 |
| INTER-03 | Coordinator emits warning event on timeout | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SteeringTimeout" --no-build` | No - Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~Steering" -x`
- **Per wave merge:** `dotnet test tests/AgentHub.Tests`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `tests/AgentHub.Tests/SteeringTests.cs` -- covers INTER-01, INTER-02, INTER-03 (coordinator, event emission, formatting)
- [ ] `tests/AgentHub.Tests/SteeringDeliveryTests.cs` -- covers INTER-03 (backend delivery confirmation, timeout)
- [ ] Existing test helpers (Helpers/ directory) likely sufficient for mocking -- no new shared fixtures expected

## Sources

### Primary (HIGH confidence)
- Direct codebase inspection of all referenced files (Models.cs, SessionCoordinator.cs, SshBackend.cs, HostDaemonModels.cs, HostCommandProtocol.cs, SessionWatchCommand.cs, SessionDetail.razor, TerminalOutput.razor, FleetOverview.razor, DurableEventService.cs, ApprovalService.cs, AgentHubApiClient.cs, DashboardApiClient.cs, Program.cs)
- CONTEXT.md phase decisions from user discussion session

### Secondary (MEDIUM confidence)
- Architecture patterns inferred from existing ApprovalService request/response tracking

### Tertiary (LOW confidence)
- None -- all findings verified from codebase

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - all libraries already in project, no new dependencies needed
- Architecture: HIGH - extending existing patterns (SendInput pipeline, HostCommand protocol, event emission)
- Pitfalls: HIGH - based on actual code patterns observed (Spectre.Console Live, SSH connection lifecycle, record constructors)

**Research date:** 2026-03-10
**Valid until:** 2026-04-10 (stable -- internal project, no external dependency changes)
