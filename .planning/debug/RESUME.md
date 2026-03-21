# Debug Session Resume — SSH Session Streaming

## Status
Session execution via SSH partially working. Multiple bugs fixed this session, one remaining blocker.

## What Was Fixed This Session

1. **SessionEvent JSON deserialization** — Client sent enum strings, server expected integers. Added global `JsonStringEnumConverter` in `Program.cs` via `ConfigureHttpJsonOptions`.

2. **Strix SSH SocketException** — `ssh://` prefix not stripped in polling services. Centralized stripping in `SshHostConnectionFactory.Create()`.

3. **FK constraint on Events INSERT** — Host metric/inventory events emitted with empty `SessionId`. `DurableEventService.EmitAsync` now skips DB persistence for sessionless events (broadcast-only).

4. **Metrics empty output on Windows** — PowerShell execution policy blocks profile via SSH. Switched to `-EncodedCommand` with base64 in both `HostMetricPollingService.GetMetricCommand()` and `HostInventoryPollingService.BuildWindowsCommand()`.

5. **Session launch 400** — `LaunchDialog.razor` hardcoded `ExecutionMode.Ssh`. Changed to `ExecutionMode.Auto` (routes to SSH when TargetHostId is set). Also fixed global JSON enum serialization.

6. **No session output (daemon was one-shot)** — `agenthub-daemon.ps1` starts agent process detached then exits; nobody reads stdout. Replaced daemon-based execution with direct SSH command execution. Agent CLI command built directly in `SshBackend.BuildAgentCommand()`.

7. **SSH.NET output buffering** — `CreateCommand` + `BeginExecute` buffers all output until command completes. Switched to `ShellStream` (PTY-allocated) in `SshHostConnection.StartStreamingCommandAsync()` for real-time output.

8. **Heartbeat timeout killing sessions** — `SessionMonitorService` killed sessions after 90s of no events. Added heartbeat emission every ~30s from the ShellStream idle loop.

## Remaining Blocker

**ShellStream output not reaching the UI.** The session stays alive (heartbeats work after latest fix) but no `StdOut` events are emitted. Claude IS running on the remote host (verified via direct SSH test taking 120+ seconds), but `StartStreamingCommandAsync` either:
- Filters out all lines (ANSI stripping + prompt/echo filtering too aggressive)
- Never receives lines from the ShellStream (banner draining issue)
- Exits prematurely before claude produces output

### Next Debugging Step

Add raw logging inside `StartStreamingCommandAsync` in `SshHostConnection.cs` right after the `ReadLine` call:

```csharp
var line = shell.ReadLine(TimeSpan.FromSeconds(5));

// ADD THIS: Log every raw line before any filtering
if (line is not null)
    _logger?.LogDebug("ShellStream raw line: [{Line}]", line);
```

This requires passing `ILogger` into the method or using the existing `_logger` field (already available on `SshHostConnection`). This will reveal exactly what the ShellStream receives and whether filtering is the issue or data never arrives.

### Alternative Approach If ShellStream Fails

If ShellStream proves unreliable for Windows SSH, consider:
1. **Use `CreateCommand.Execute()` (blocking)** in a background thread — output arrives all at once when claude finishes, but at least it works
2. **Use PowerShell wrapper** to force line-buffered output: `powershell -NoProfile -Command "& { claude --print ... } | ForEach-Object { $_ }"`
3. **Write output to temp file on remote host** and tail it from a separate SSH channel

## Key Files Modified

- `src/AgentHub.Service/Program.cs` — Global `JsonStringEnumConverter`, new exception catches
- `src/AgentHub.Orchestration/Backends/SshHostConnection.cs` — `StartStreamingCommandAsync` (ShellStream), `StripAnsi`, factory prefix stripping
- `src/AgentHub.Orchestration/Backends/SshBackend.cs` — `BuildAgentCommand`, `ReadAgentOutputAsync`, removed daemon-based execution
- `src/AgentHub.Orchestration/Events/DurableEventService.cs` — Skip DB for sessionless events
- `src/AgentHub.Orchestration/Monitoring/HostMetricPollingService.cs` — `-EncodedCommand` for Windows
- `src/AgentHub.Orchestration/Monitoring/HostInventoryPollingService.cs` — `-EncodedCommand` for Windows
- `src/AgentHub.Web/Components/Shared/LaunchDialog.razor` — `ExecutionMode.Auto`, AcceptRisk UX
- `src/AgentHub.Web/Services/DashboardApiClient.cs` — `JsonStringEnumConverter`
- `src/AgentHub.Web/Services/SseStreamService.cs` — `JsonStringEnumConverter`
- `src/AgentHub.Cli/Api/AgentHubApiClient.cs` — `JsonStringEnumConverter`
- `src/AgentHub.Cli/Api/SseStreamReader.cs` — `JsonStringEnumConverter`

## Environment Notes

- Host "Strix" is `ssh://strix` — the local Windows machine accessible via SSH
- SSH default shell is cmd.exe (not PowerShell)
- PowerShell requires `-NoProfile -ExecutionPolicy Bypass` when invoked via SSH
- SSH.NET's `CreateCommand` does NOT stream output in real-time (buffers until completion)
- Claude Code `--print` flag works via SSH but takes 60-120+ seconds for inference
- Running tests from within Claude Code causes Bash tool to intercept `claude` commands — use `python3 subprocess` for clean SSH testing
