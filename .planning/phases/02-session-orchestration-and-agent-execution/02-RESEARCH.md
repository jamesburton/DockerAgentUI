# Phase 2: Session Orchestration and Agent Execution - Research

**Researched:** 2026-03-08
**Domain:** Session lifecycle management, SSH remote execution, approval gating, config parsing
**Confidence:** HIGH

## Summary

Phase 2 transforms the stub SshBackend into a real SSH execution backend, implements the full session lifecycle (launch, monitor, stop, fire-and-forget), builds the approval/elevation gating flow, and adds multi-format skill/policy configuration. The existing codebase provides strong foundations: ISessionBackend interface, HostCommandProtocol with single-line JSON, DurableEventService with SSE broadcasting, BasicSanitizationService, and the SessionCoordinator orchestration layer. All of these are built in Phase 1 and are functional but delegate to stubs for actual remote execution.

The primary technical challenges are: (1) managing long-lived SSH connections with event streaming back to the coordinator, (2) implementing the two-phase stop (SIGINT then SIGTERM) over SSH, (3) building the approval flow as SSE events with configurable timeout behavior, and (4) supporting three config formats (JSON, YAML, Markdown) with scoping hierarchy. The existing code already persists sessions and events to SQLite via EF Core, so session history (SESS-05) is largely infrastructure work on top of what exists.

**Primary recommendation:** Implement SshBackend using SSH.NET (2025.1.0) for remote process execution, add a SessionMonitorService (BackgroundService) for heartbeat monitoring, and extend the event model with ApprovalRequest/ApprovalResponse event kinds for the gating flow.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- User provides either a prompt string OR a file reference (spec, PLAN.md, etc.) -- agent receives whichever is given
- Coordinator auto-selects host via placement engine by default; user can pin a specific host with `--host` flag
- Fire-and-forget task sessions: host daemon pushes events back over SSH channel + coordinator sends periodic heartbeat pings to detect dead connections
- Each session gets its own git worktree on the remote host for isolation -- prevents conflicts when multiple agents work on the same repo
- Approval requests delivered as SSE events to all connected clients (CLI, web) via existing DurableEventService
- Approval timeout behavior is configurable at four scoping levels: default -> host -> project -> task
  - Default: pause session indefinitely until someone responds
  - With `dangerously-skip-permissions`: auto-approve without prompting
  - Configurable timeout actions: continue with workarounds, auto-approve, or stop session
- Approval requests include detailed context -- the specific action (command, file path, diff preview if available)
- Action-level trust tiers defined in policy config: always-allow (reads, ls), prompt (writes, deletes), always-deny (destructive operations)
- Two-phase stop: SIGINT first (graceful, lets agent commit WIP), wait N seconds, then SIGTERM if still running
- Force-kill available as separate explicit action (bypasses graceful phase)
- Cleanup is configurable per session: default is clean up worktree + temp files on success, keep on failure/kill. User can override with `--keep` or `--cleanup` flags
- Orphaned session detection via heartbeat timeout -- if no heartbeat or event received within N seconds, mark session as failed/orphaned
- No maximum session duration by default, but optional `--time-limit` flag per session to set one when needed
- Support three config formats: JSON, YAML, and Markdown -- auto-detect by file extension
- Markdown is the default format, following a structured frontmatter + content pattern for copy-to-deploy sharing
- Config scoping hierarchy: default -> host -> project -> task. Last wins (most specific scope overrides)
- Sanitization layer at API boundary -- coordinator validates/sanitizes inputs when receiving StartSession or SendInput requests via existing BasicSanitizationService
- Full stdout/stderr stored per session as SessionEvents in the DB -- user can replay complete session history

### Claude's Discretion
- Git worktree creation/cleanup implementation details on remote hosts
- Heartbeat interval and timeout defaults
- SIGINT-to-SIGTERM wait duration
- Markdown config parsing implementation (frontmatter extraction)
- Trust tier definitions and default policy rules
- Sanitization rule specifics and injection patterns to check

### Deferred Ideas (OUT OF SCOPE)
- SpacetimeDB as EF Core provider alternative -- evaluate when needed (from Phase 1)
- Database-backed config persistence (vs file-based) -- future iteration (from Phase 1)
- MCP as primary agent control protocol -- evaluate after SSH execution proves the pattern (from Phase 1)
- AI-generated session summaries -- store full output first, add summarization later
- Multi-agent coordination on shared codebases -- v2 requirement (SESS-07)
- Interactive bidirectional sessions -- v2 requirement (SESS-06)
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| SESS-01 | User can launch an agent session on a registered remote host | SshBackend implementation using SSH.NET to send start-session command via HostCommandProtocol, stream events back over SSH stdout |
| SESS-02 | User can view status of all running sessions across all hosts | SessionEntity already persisted to DB; SshBackend.ListAsync needs to query DB instead of ConcurrentDictionary; add status endpoint aggregation |
| SESS-03 | User can stop a running session (graceful + force-kill) | Two-phase stop: send stop-session command with graceful flag (SIGINT), wait configurable duration, then force-kill (SIGTERM); new ForceKill command in HostCommandProtocol |
| SESS-04 | User can launch a fire-and-forget task session that runs to completion | Same as SESS-01 but session runs without interactive input; heartbeat monitoring detects completion/failure via SessionMonitorService |
| SESS-05 | User can review past session history with stored output and outcome | Events already persisted via DurableEventService; add query endpoints for historical sessions with pagination and filtering |
| AGENT-02 | Skills and policies are defined via configuration files (YAML/JSON) | Multi-format config loader supporting JSON (existing), YAML (YamlDotNet), and Markdown (frontmatter extraction); scoping hierarchy with merge |
| AGENT-03 | Inputs to agents pass through a configurable sanitization layer | Extend BasicSanitizationService with configurable rules loaded from policy config; add injection pattern detection |
| AGENT-04 | Destructive agent actions trigger an approval/elevation flow requiring human confirmation | New ApprovalRequest/ApprovalResponse event kinds; approval state machine per session; SSE delivery via DurableEventService; configurable timeout |
| AGENT-05 | User can set permission-skip flags per agent | Already partially implemented in AgentPermissions/PermissionMerger; wire through StartSessionRequest -> HostCommandProtocol -> host daemon |
</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| SSH.NET | 2025.1.0 | SSH connection management and remote command execution | De facto .NET SSH library; optimized for parallelism; supports shell, command, and forwarded port channels |
| YamlDotNet | 16.3.0 | YAML config file parsing and deserialization | Standard .NET YAML library; supports deserialization to strongly-typed objects |
| Markdig | 0.38.0+ | Markdown frontmatter extraction | Standard .NET Markdown parser; has YAML frontmatter extension |
| CliWrap | 3.10.0 | Local agent process execution (already in project) | Already used by ClaudeCodeAdapter |
| EF Core SQLite | 10.0.2 | Session/event persistence (already in project) | Already configured with Pool + Factory pattern |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| System.Threading.Channels | built-in | Event streaming between SSH reader and DurableEventService | All session event forwarding |
| System.Text.Json | built-in | HostCommandProtocol serialization (already used) | All daemon communication |
| Microsoft.Extensions.Hosting | 10.0.3 | BackgroundService for heartbeat monitor | SessionMonitorService |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| SSH.NET | System.Diagnostics.Process + ssh CLI | SSH.NET gives programmatic channel control; Process requires ssh binary on host and offers less control over streams |
| YamlDotNet | SharpYaml | YamlDotNet has wider adoption and better docs; SharpYaml is lighter but less maintained |
| Markdig for frontmatter | Manual string splitting | Markdig handles edge cases (triple-dash within code blocks); manual is fragile |

**Installation:**
```bash
dotnet add src/AgentHub.Orchestration/AgentHub.Orchestration.csproj package SSH.NET --version 2025.1.0
dotnet add src/AgentHub.Orchestration/AgentHub.Orchestration.csproj package YamlDotNet --version 16.3.0
dotnet add src/AgentHub.Orchestration/AgentHub.Orchestration.csproj package Markdig --version 0.38.0
```

## Architecture Patterns

### Recommended Project Structure
```
src/AgentHub.Orchestration/
├── Backends/
│   ├── SshBackend.cs               # REWRITE: real SSH execution via SSH.NET
│   └── InMemoryBackend.cs          # Keep as-is for local dev/testing
├── Coordinator/
│   ├── SessionCoordinator.cs       # EXTEND: add force-kill, session DB persistence
│   └── ApprovalService.cs          # NEW: approval state machine + timeout handling
├── Config/
│   ├── PolicyModels.cs             # EXTEND: trust tiers, scoped config
│   ├── SkillPolicyService.cs       # REWRITE: multi-format loader with scoping
│   ├── SkillRegistry.cs            # EXTEND: multi-format support
│   ├── ConfigLoader.cs             # NEW: unified JSON/YAML/MD config loader
│   └── ConfigScopeMerger.cs        # NEW: scoping hierarchy merge logic
├── HostDaemon/
│   ├── HostCommandProtocol.cs      # EXTEND: add force-kill, approval commands
│   └── HostDaemonModels.cs         # EXTEND: approval payloads, cleanup config
├── Monitoring/
│   └── SessionMonitorService.cs    # NEW: BackgroundService for heartbeat/orphan detection
├── Security/
│   └── BasicSanitizationService.cs # EXTEND: configurable rules from policy config
├── Events/
│   ├── DurableEventService.cs      # As-is (foundation ready)
│   └── SseSubscriptionManager.cs   # As-is (foundation ready)
└── Data/
    ├── Entities/
    │   ├── SessionEntity.cs        # EXTEND: add CompletedUtc, ExitCode, CleanupState
    │   └── ApprovalEntity.cs       # NEW: approval request persistence
    └── AgentHubDbContext.cs         # EXTEND: add Approvals DbSet
```

### Pattern 1: SSH Session Execution via SSH.NET Shell Stream
**What:** Use SSH.NET's ShellStream to maintain a persistent connection to the host daemon, sending JSON commands over stdin and reading JSON events from stdout.
**When to use:** All remote session execution (SESS-01, SESS-04).
**Example:**
```csharp
// SshBackend connects to host daemon via SSH.NET
using Renci.SshNet;

public async Task<string> StartAsync(string ownerUserId, StartSessionRequest request,
    PlacementDecision placement, Func<SessionEvent, Task> emit, CancellationToken ct)
{
    var host = await _hostRegistry.GetAsync(placement.NodeId, ct);
    var connectionInfo = new ConnectionInfo(host.Address, "agent-user",
        new PrivateKeyAuthenticationMethod("agent-user",
            new PrivateKeyFile(_keyPath)));

    var client = new SshClient(connectionInfo);
    client.Connect();

    var sessionId = $"ssh_{Guid.NewGuid():N}";
    var command = HostCommandProtocol.CreateStartSession(sessionId,
        new StartSessionPayload { /* ... */ });

    // Send command, read events from stdout line by line
    var shellStream = client.CreateShellStream("daemon", 0, 0, 0, 0, 4096);
    shellStream.WriteLine(HostCommandProtocol.Serialize(command));

    // Background task reads events from shell stream
    _ = Task.Run(async () => {
        using var reader = new StreamReader(shellStream);
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            var response = HostCommandProtocol.DeserializeResponse(line);
            await emit(MapToSessionEvent(sessionId, response));
        }
    }, ct);

    // Persist session to DB
    await PersistSessionAsync(sessionId, ownerUserId, placement, request, ct);
    return sessionId;
}
```

### Pattern 2: Approval State Machine
**What:** Each approval request creates an ApprovalEntity in DB, emits an SSE event, and blocks the session until resolved (approved/denied/timed-out).
**When to use:** AGENT-04 destructive action gating.
**Example:**
```csharp
public sealed class ApprovalService
{
    // Pending approvals: sessionId -> TaskCompletionSource<ApprovalDecision>
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ApprovalDecision>>
        _pending = new();

    public async Task<ApprovalDecision> RequestApprovalAsync(
        string sessionId, ApprovalContext context,
        Func<SessionEvent, Task> emit, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<ApprovalDecision>();
        var approvalId = Guid.NewGuid().ToString("N");
        _pending[approvalId] = tcs;

        // Emit approval request event (delivered to all SSE clients)
        await emit(new SessionEvent(sessionId, SessionEventKind.ApprovalRequest,
            DateTimeOffset.UtcNow,
            JsonSerializer.Serialize(context),
            new Dictionary<string, string> { ["approvalId"] = approvalId }));

        // Wait for response or timeout
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (context.TimeoutSeconds > 0)
            cts.CancelAfter(TimeSpan.FromSeconds(context.TimeoutSeconds));

        try { return await tcs.Task.WaitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            return context.TimeoutAction; // configured default action
        }
        finally { _pending.TryRemove(approvalId, out _); }
    }

    public void ResolveApproval(string approvalId, ApprovalDecision decision)
    {
        if (_pending.TryGetValue(approvalId, out var tcs))
            tcs.TrySetResult(decision);
    }
}
```

### Pattern 3: Multi-Format Config Loader with Scoping
**What:** A unified config loader that detects format by file extension (.json, .yaml/.yml, .md) and merges configs using the scoping hierarchy (default -> host -> project -> task).
**When to use:** AGENT-02 skill/policy configuration.
**Example:**
```csharp
public sealed class ConfigLoader
{
    public T Load<T>(string path) where T : class, new()
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var content = File.ReadAllText(path);
        return ext switch
        {
            ".json" => JsonSerializer.Deserialize<T>(content, JsonOpts) ?? new T(),
            ".yaml" or ".yml" => new DeserializerBuilder().Build()
                .Deserialize<T>(content) ?? new T(),
            ".md" => ParseMarkdownFrontmatter<T>(content),
            _ => throw new NotSupportedException($"Config format {ext} not supported")
        };
    }

    private T ParseMarkdownFrontmatter<T>(string content) where T : class, new()
    {
        // Extract YAML between --- delimiters
        var match = Regex.Match(content, @"^---\s*\n(.*?)\n---", RegexOptions.Singleline);
        if (!match.Success) return new T();
        var yaml = match.Groups[1].Value;
        return new DeserializerBuilder().Build().Deserialize<T>(yaml) ?? new T();
    }
}
```

### Pattern 4: Two-Phase Stop with Timeout
**What:** Send SIGINT via stop-session command, wait N seconds, then send force-kill if still running.
**When to use:** SESS-03 graceful + force stop.
**Example:**
```csharp
public async Task StopAsync(string sessionId, bool forceKill, CancellationToken ct)
{
    if (forceKill)
    {
        await SendCommand(sessionId, HostCommandProtocol.CreateForceKill(sessionId), ct);
        return;
    }

    // Phase 1: graceful (SIGINT)
    await SendCommand(sessionId, HostCommandProtocol.CreateStopSession(sessionId), ct);

    // Phase 2: wait then force-kill
    var gracePeriod = TimeSpan.FromSeconds(10); // configurable
    await Task.Delay(gracePeriod, ct);

    var status = await GetSessionStatusAsync(sessionId, ct);
    if (status.State == SessionState.Running)
    {
        await SendCommand(sessionId, HostCommandProtocol.CreateForceKill(sessionId), ct);
    }
}
```

### Anti-Patterns to Avoid
- **Storing SSH connections in DI singleton state:** SSH connections are inherently stateful and fragile. Track them in a dedicated connection manager with reconnection logic, not bare ConcurrentDictionary entries.
- **Blocking on approval responses synchronously:** The approval flow must be async with TaskCompletionSource; never block a thread waiting for user input.
- **Hardcoding config paths:** Use the scoping hierarchy (default -> host -> project -> task) and auto-discover config files by convention.
- **Mixing session state between in-memory and DB:** Phase 2 must consolidate session state into the DB. The current SshBackend uses ConcurrentDictionary; this must be replaced with DB-backed state via SessionEntity.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| SSH connections | Raw TcpClient + SSH handshake | SSH.NET SshClient/SshCommand | Key exchange, channel multiplexing, keepalive are complex; SSH.NET handles all |
| YAML parsing | Regex-based YAML parser | YamlDotNet Deserializer | YAML spec has edge cases (multiline strings, anchors, type coercion) that break custom parsers |
| Markdown frontmatter | String.Split("---") | Regex extraction + YamlDotNet | Need to handle code blocks containing "---", UTF-8 BOM, Windows line endings |
| Process signal handling (remote) | Custom signal protocol | Host daemon wraps Process.Kill / SIGINT sending | OS-specific signal semantics differ between Windows and Linux |
| Event replay on reconnect | Custom replay logic | DurableEventService (already built) | Already handles Last-Event-ID based replay from DB |
| SSE broadcasting | Custom pub/sub | SseSubscriptionManager (already built) | Already handles subscriber lifecycle with concurrent cleanup |

**Key insight:** The existing Phase 1 infrastructure (DurableEventService, SseSubscriptionManager, HostCommandProtocol) was designed to support Phase 2. The primary work is wiring real SSH connections through these existing abstractions, not rebuilding them.

## Common Pitfalls

### Pitfall 1: SSH Connection Lifecycle Mismanagement
**What goes wrong:** SSH connections drop silently (network issues, idle timeouts), and the coordinator doesn't detect it until the next command fails.
**Why it happens:** SSH.NET doesn't always raise events for silent disconnects; TCP keepalive may not be configured.
**How to avoid:** Use SSH.NET's `KeepAliveInterval` property (set to 15-30s). Implement heartbeat pings at the application level (the decision calls for this). Register `client.ErrorOccurred` and `client.ConnectionInfo.AuthenticationBanner` events.
**Warning signs:** Sessions stuck in "Running" state with no events for extended periods.

### Pitfall 2: Concurrent DB Access from Singleton Services
**What goes wrong:** DurableEventService (singleton) and SshBackend (singleton) both need DB access, but DbContext is not thread-safe.
**Why it happens:** EF Core DbContext instances cannot be shared across threads.
**How to avoid:** Already mitigated by the IDbContextFactory pattern from Phase 1. Continue using `await _dbFactory.CreateDbContextAsync()` for every DB operation in singleton services. Never cache a DbContext instance.
**Warning signs:** `InvalidOperationException` about concurrent operations on the same DbContext.

### Pitfall 3: Approval Timeout Race Conditions
**What goes wrong:** User approves at the exact moment the timeout fires, leading to both paths executing.
**Why it happens:** TaskCompletionSource.TrySetResult and CancellationToken firing are not atomic.
**How to avoid:** Use `TrySetResult` (not `SetResult`) and check the return value. The first to complete wins.
**Warning signs:** Sessions resuming after timeout with unexpected state.

### Pitfall 4: Config Merge Order Ambiguity
**What goes wrong:** Configs from different scopes merge unpredictably, especially for list properties (tool allow/deny lists).
**Why it happens:** "Last wins" is clear for scalar values but ambiguous for collections.
**How to avoid:** Define explicit merge semantics: scalars use last-wins, lists use union by default, deny-lists use union (any scope can deny), allow-lists use intersection or last-wins (document which). Follow the Phase 1 precedent: "session overrides win for tool lists, OR logic for SkipPermissionPrompts."
**Warning signs:** Users surprised that a tool is allowed/denied when they expected the opposite.

### Pitfall 5: Git Worktree Cleanup Races
**What goes wrong:** Cleanup runs while the agent process is still writing to the worktree (e.g., during the SIGINT grace period).
**Why it happens:** The two-phase stop hasn't fully completed before cleanup triggers.
**How to avoid:** Only trigger cleanup after the agent process has definitively exited (Completion task resolved). Never cleanup during the grace period.
**Warning signs:** Partial file deletions, git lock file errors.

### Pitfall 6: Shell Stream vs Command Execution
**What goes wrong:** Using SSH.NET's `CreateShellStream` when `RunCommand` is more appropriate, leading to prompt artifacts in output.
**Why it happens:** ShellStream includes shell initialization (PS1 prompt, .bashrc output) mixed with command output.
**How to avoid:** Use `SshClient.RunCommand()` for one-shot commands. For the host daemon (persistent process), use `SshClient.CreateCommand()` with piped stdin/stdout, or better: launch the daemon process via `RunCommand` and read its stdout/stderr streams directly.
**Warning signs:** Output contains bash prompt characters, ANSI escape codes, or shell initialization messages.

## Code Examples

### SSH.NET Connection with Heartbeat
```csharp
// Connection management for a single host
public sealed class SshHostConnection : IAsyncDisposable
{
    private readonly SshClient _client;
    private readonly Timer _heartbeat;
    private volatile bool _connected;

    public SshHostConnection(string host, string username, string privateKeyPath)
    {
        var keyFile = new PrivateKeyFile(privateKeyPath);
        _client = new SshClient(host, username, keyFile);
        _client.KeepAliveInterval = TimeSpan.FromSeconds(15);
        _client.ErrorOccurred += (_, e) => OnError(e.Exception);
        _heartbeat = new Timer(SendHeartbeat, null, Timeout.Infinite, Timeout.Infinite);
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        await Task.Run(() => _client.Connect(), ct);
        _connected = true;
        _heartbeat.Change(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public async Task<string> ExecuteCommandAsync(string commandJson, CancellationToken ct)
    {
        var cmd = _client.CreateCommand(commandJson);
        return await Task.Run(() => cmd.Execute(), ct);
    }

    private void SendHeartbeat(object? state)
    {
        if (!_connected || !_client.IsConnected) return;
        try
        {
            var ping = HostCommandProtocol.Serialize(HostCommandProtocol.CreatePing());
            var cmd = _client.CreateCommand(ping);
            cmd.Execute();
        }
        catch { _connected = false; }
    }

    private void OnError(Exception ex) => _connected = false;

    public async ValueTask DisposeAsync()
    {
        await _heartbeat.DisposeAsync();
        _client.Disconnect();
        _client.Dispose();
    }
}
```

### New SessionEventKind Values for Approval Flow
```csharp
// Extend the existing SessionEventKind enum
public enum SessionEventKind
{
    Info,
    StdOut,
    StdErr,
    StateChanged,
    Metric,
    Custom,
    Policy,
    Threat,
    Audit,
    // Phase 2 additions:
    ApprovalRequest,    // Emitted when agent hits a destructive action
    ApprovalResponse,   // Emitted when user approves/denies
    Heartbeat,          // Periodic heartbeat confirmation
    SessionCompleted,   // Fire-and-forget task finished
    CleanupStarted,     // Worktree cleanup initiated
    CleanupCompleted    // Worktree cleanup finished
}
```

### SessionEntity Extensions for Phase 2
```csharp
// Extend existing SessionEntity
public class SessionEntity
{
    // ... existing properties ...

    // Phase 2 additions:
    public DateTimeOffset? CompletedUtc { get; set; }
    public int? ExitCode { get; set; }
    public string? CleanupState { get; set; } // "pending", "completed", "skipped", "failed"
    public bool IsFireAndForget { get; set; }
    public string? Prompt { get; set; }        // Store the original prompt for history
    public string? TimeLimit { get; set; }     // Optional time limit (TimeSpan serialized)
    public string? CleanupPolicy { get; set; } // "auto", "keep", "cleanup"
}
```

### Multi-Format Config Discovery
```csharp
// Discover config files across scoping hierarchy
public static class ConfigDiscovery
{
    private static readonly string[] Extensions = [".md", ".yaml", ".yml", ".json"];

    public static IReadOnlyList<string> FindConfigFiles(
        string configName, string configRoot, string? hostId, string? projectId, string? taskId)
    {
        var paths = new List<string>();

        // Scoping: default -> host -> project -> task (last wins)
        var scopes = new List<string> { Path.Combine(configRoot, "defaults") };
        if (hostId is not null) scopes.Add(Path.Combine(configRoot, "hosts", hostId));
        if (projectId is not null) scopes.Add(Path.Combine(configRoot, "projects", projectId));
        if (taskId is not null) scopes.Add(Path.Combine(configRoot, "tasks", taskId));

        foreach (var scope in scopes)
        {
            foreach (var ext in Extensions)
            {
                var path = Path.Combine(scope, configName + ext);
                if (File.Exists(path)) paths.Add(path);
            }
        }

        return paths;
    }
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| SSH.NET 2024.0.0 | SSH.NET 2025.1.0 | 2025 | Performance improvements for parallel operations |
| YamlDotNet 15.x | YamlDotNet 16.3.0 | 2025 | Better .NET 8/9/10 support, improved nullable handling |
| Manual SSH process mgmt | CliWrap + SSH.NET hybrid | Current | CliWrap for local agents, SSH.NET for remote; avoids mixing paradigms |

**Deprecated/outdated:**
- SSH.NET `ForwardedPort` for daemon communication: unnecessary; direct command execution is simpler for this use case
- `SshCommand.BeginExecute`/`EndExecute`: use async wrappers around `Execute()` instead (SSH.NET doesn't have native async yet, wrap with Task.Run)

## Open Questions

1. **SSH.NET async support limitations**
   - What we know: SSH.NET 2025.1.0 still uses synchronous APIs internally; async is achieved via Task.Run wrappers
   - What's unclear: Whether this causes thread pool starvation at scale (10+ concurrent sessions)
   - Recommendation: Start with Task.Run wrappers; profile under load; if problematic, consider spawning ssh CLI via CliWrap as fallback

2. **Host daemon deployment and lifecycle**
   - What we know: HostCommandProtocol defines the command format; daemon reads stdin line-by-line
   - What's unclear: How the daemon process is started on the remote host (pre-installed? deployed on first connect?)
   - Recommendation: Assume daemon is pre-installed on registered hosts (matches Phase 1 decision that operator installs tools manually). Add a `verify-daemon` command to HostCommandProtocol for health checking.

3. **Markdown config body usage**
   - What we know: Frontmatter contains structured config; body is Markdown content
   - What's unclear: Whether the body (non-frontmatter part) should be passed to the agent as system prompt / instructions
   - Recommendation: Store body as a `content` field alongside parsed frontmatter; let the agent adapter decide whether to use it (e.g., as additional context in the prompt).

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xunit 2.9.x with Microsoft.NET.Test.Sdk 17.x |
| Config file | tests/AgentHub.Tests/AgentHub.Tests.csproj (exists) |
| Quick run command | `dotnet test tests/AgentHub.Tests --filter "Category!=Integration" -x` |
| Full suite command | `dotnet test tests/AgentHub.Tests` |

### Phase Requirements -> Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| SESS-01 | Launch session on remote host (mocked SSH) | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SshBackendTests.StartSession"` | No - Wave 0 |
| SESS-02 | List sessions across backends | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SessionCoordinatorTests.ListSessions"` | No - Wave 0 |
| SESS-03 | Stop session graceful + force-kill | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SshBackendTests.StopSession"` | No - Wave 0 |
| SESS-04 | Fire-and-forget task session completes | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SshBackendTests.FireAndForget"` | No - Wave 0 |
| SESS-05 | Query past session history | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SessionHistoryTests"` | No - Wave 0 |
| AGENT-02 | Config loading JSON/YAML/MD | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~ConfigLoaderTests"` | No - Wave 0 |
| AGENT-03 | Sanitization blocks dangerous input | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SanitizationTests"` | No - Wave 0 |
| AGENT-04 | Approval flow blocks destructive actions | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~ApprovalServiceTests"` | No - Wave 0 |
| AGENT-05 | Permission-skip flags pass through | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~PermissionFlagTests"` | No - Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test tests/AgentHub.Tests --filter "Category!=Integration" --no-build -x`
- **Per wave merge:** `dotnet test tests/AgentHub.Tests`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `tests/AgentHub.Tests/SshBackendTests.cs` -- covers SESS-01, SESS-03, SESS-04 (mock SSH.NET client)
- [ ] `tests/AgentHub.Tests/SessionCoordinatorTests.cs` -- covers SESS-02 (DB-backed listing)
- [ ] `tests/AgentHub.Tests/SessionHistoryTests.cs` -- covers SESS-05 (query/pagination)
- [ ] `tests/AgentHub.Tests/ConfigLoaderTests.cs` -- covers AGENT-02 (JSON/YAML/MD parsing + scope merge)
- [ ] `tests/AgentHub.Tests/SanitizationTests.cs` -- covers AGENT-03 (extended sanitization rules)
- [ ] `tests/AgentHub.Tests/ApprovalServiceTests.cs` -- covers AGENT-04 (approval flow + timeout)
- [ ] `tests/AgentHub.Tests/PermissionFlagTests.cs` -- covers AGENT-05 (skip-permissions wiring)
- [ ] `tests/AgentHub.Tests/Helpers/MockSshClient.cs` -- shared mock for SSH.NET (avoids real SSH in unit tests)

## Sources

### Primary (HIGH confidence)
- Existing codebase analysis: all source files in src/AgentHub.Orchestration/ and src/AgentHub.Contracts/
- Phase 1 implementation: established patterns (DI, DbContext, event model, host protocol)
- CONTEXT.md: locked decisions constrain architecture

### Secondary (MEDIUM confidence)
- [SSH.NET NuGet 2025.1.0](https://www.nuget.org/packages/ssh.net/) - version and availability confirmed
- [YamlDotNet NuGet 16.3.0](https://www.nuget.org/packages/YamlDotNet) - version confirmed
- [SSH.NET GitHub](https://github.com/sshnet/SSH.NET) - feature overview
- [Markdown frontmatter parsing in C#](https://khalidabuhakmeh.com/parse-markdown-front-matter-with-csharp) - pattern reference
- [BackgroundService pattern](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services) - official Microsoft docs

### Tertiary (LOW confidence)
- SSH.NET async behavior under load: based on general knowledge of the library; needs profiling validation

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - libraries verified on NuGet, versions confirmed
- Architecture: HIGH - building on established Phase 1 patterns with well-defined interfaces
- Pitfalls: HIGH - derived from code analysis of existing stubs and known .NET concurrency patterns
- SSH.NET async performance: LOW - needs real profiling; training data may be stale

**Research date:** 2026-03-08
**Valid until:** 2026-04-08 (stable domain; SSH.NET and YamlDotNet don't change rapidly)

**Discretionary recommendations (Claude's discretion areas):**
- **Heartbeat interval:** 30 seconds; timeout: 90 seconds (3 missed heartbeats). Aggressive enough to catch failures within 2 minutes.
- **SIGINT-to-SIGTERM wait:** 10 seconds. Enough time for `git commit` but not long enough to leave zombie processes.
- **Trust tier defaults:** always-allow = [Read, Glob, Grep, LS, WebSearch]; prompt = [Write, Edit, Bash, WebFetch]; always-deny = [rm -rf /, format, shutdown]. These align with the existing BasicSanitizationService patterns.
- **Git worktree on remote:** `git worktree add /sessions/{sessionId} -b session/{sessionId}` for creation; `git worktree remove` + branch delete for cleanup. Only clean up after Completion task resolves and exit code is 0 (or user explicitly requests cleanup via --cleanup).
- **Markdown config frontmatter:** Use regex extraction between `---` delimiters, then YamlDotNet deserialization. Body content stored as `Instructions` field, passed to agent as additional context alongside the prompt.
- **Sanitization extensions:** Add patterns for: shell injection (`;`, `&&`, `||`, backticks, `$()`), path traversal (`../`), environment variable exfiltration (`$ENV_VAR` in suspicious contexts), and encoded payloads (base64-encoded dangerous commands).
