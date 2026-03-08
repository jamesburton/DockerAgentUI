---
phase: 03-cli-client
plan: 02
subsystem: cli
tags: [sse, spectre-console-live, streaming, notifications, approval-handler, dotnet]

requires:
  - phase: 03-cli-client
    provides: CLI project with AgentHubApiClient, IOutputFormatter, CliConfig, command tree
provides:
  - SseStreamReader with automatic reconnection and Last-Event-ID support
  - LiveDisplayManager for thread-safe Spectre.Console Live context
  - Session watch command with color-coded live event streaming
  - Session logs command with --all, --tail, --follow, --kind options
  - Fleet watch command for live overview of all running sessions
  - Host status --watch mode for periodic metrics refresh
  - Listen command for background notification stream
  - NotificationService with file-based persistence and thread-safe locking
  - ApprovalPromptHandler for inline approval during session watch
  - Pending notification summary on every CLI command invocation
affects: [03-cli-client]

tech-stack:
  added: [System.Net.ServerSentEvents (in-box .NET 10)]
  patterns: [Channel-based SSE streaming with yield return bridge, file-lock notification persistence, Live display pause/resume for approval prompts]

key-files:
  created:
    - src/AgentHub.Cli/Api/SseStreamReader.cs
    - src/AgentHub.Cli/Output/LiveDisplayManager.cs
    - src/AgentHub.Cli/Commands/Session/SessionWatchCommand.cs
    - src/AgentHub.Cli/Commands/Session/SessionLogsCommand.cs
    - src/AgentHub.Cli/Commands/WatchCommand.cs
    - src/AgentHub.Cli/Commands/Host/HostStatusCommand.cs
    - src/AgentHub.Cli/Commands/ListenCommand.cs
    - src/AgentHub.Cli/Notifications/NotificationService.cs
    - src/AgentHub.Cli/Notifications/ApprovalPromptHandler.cs
    - tests/AgentHub.Tests/SseStreamReaderTests.cs
    - tests/AgentHub.Tests/NotificationServiceTests.cs
  modified:
    - src/AgentHub.Cli/Program.cs

key-decisions:
  - "Channel<T> bridge to avoid yield-in-try-catch C# restriction in SSE reader"
  - "In-process lock (object) for NotificationService thread safety instead of file-only lock"
  - "Notification summary shown before command execution via TableFormatter, not per-command injection"

patterns-established:
  - "SseStreamReader: Channel-based producer/consumer pattern for SSE streams with reconnection"
  - "LiveDisplayManager: Static helper for common Live table + SSE update patterns"
  - "NotificationService: File persistence at ~/.agenthub/notifications.json with lock-based concurrency"
  - "ApprovalPromptHandler: Live display pause -> prompt -> resume pattern for interactive approvals"

requirements-completed: [CLI-01, MON-02, MON-03]

duration: 10min
completed: 2026-03-08
---

# Phase 3 Plan 2: Streaming, Watch, and Notifications Summary

**SSE streaming with reconnection, live session/fleet watch dashboards, session log follow mode, notification persistence, and inline approval handling using Spectre.Console Live and System.Net.ServerSentEvents**

## Performance

- **Duration:** 10 min
- **Started:** 2026-03-08T20:34:51Z
- **Completed:** 2026-03-08T20:44:36Z
- **Tasks:** 2
- **Files modified:** 12

## Accomplishments
- SseStreamReader with automatic reconnection (max 10 retries, 2s delay) and Last-Event-ID resume support
- Five streaming/watch commands: session watch, session logs (--follow), fleet watch, host status --watch, listen
- NotificationService with file-based persistence, pending summary on every CLI invocation, and terminal bell
- ApprovalPromptHandler with Live display pause/resume, risk-level color coding, and approve/reject/skip flow
- 18 tests passing (5 SSE stream reader, 8 notification service, plus pre-existing)

## Task Commits

Each task was committed atomically:

1. **Task 1: SSE stream reader, live display, watch commands** - `c8bdbf9` (feat)
2. **Task 2: Notification service, listen command, approval handler** - `9ba127e` (feat)

## Files Created/Modified
- `src/AgentHub.Cli/Api/SseStreamReader.cs` - SSE consumption with Channel-based reconnection
- `src/AgentHub.Cli/Output/LiveDisplayManager.cs` - Thread-safe Spectre.Console Live wrapper
- `src/AgentHub.Cli/Commands/Session/SessionWatchCommand.cs` - Live session dashboard with color-coded events
- `src/AgentHub.Cli/Commands/Session/SessionLogsCommand.cs` - History viewer with --all, --tail, --follow, --kind
- `src/AgentHub.Cli/Commands/WatchCommand.cs` - Fleet-wide live overview with periodic refresh
- `src/AgentHub.Cli/Commands/Host/HostStatusCommand.cs` - --watch mode for live host metrics
- `src/AgentHub.Cli/Commands/ListenCommand.cs` - Background notification stream for notable events
- `src/AgentHub.Cli/Notifications/NotificationService.cs` - File-based notification persistence with locking
- `src/AgentHub.Cli/Notifications/ApprovalPromptHandler.cs` - Inline approval panel during session watch
- `src/AgentHub.Cli/Program.cs` - Wired all new commands, SseStreamReader factory, notification summary
- `tests/AgentHub.Tests/SseStreamReaderTests.cs` - 5 tests: parsing, fleet, empty data, reconnection, Last-Event-ID
- `tests/AgentHub.Tests/NotificationServiceTests.cs` - 8 tests: persistence, acknowledge, concurrent, summary

## Decisions Made
- Used Channel<T> producer/consumer pattern in SseStreamReader because C# does not allow `yield return` inside try-catch blocks -- the producer writes to the channel in a background task with proper exception handling, and the consumer yields from the channel reader
- Used in-process `lock` object for NotificationService thread safety rather than file-only locks -- simpler and more reliable for the single-process CLI scenario; file-level lock was insufficient for concurrent Task.Run threads
- Pending notification summary uses TableFormatter directly (not per-command formatter resolution) since it runs before command parsing and should always show as human-readable text

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Missing Spectre.Console.Rendering using directive for IRenderable**
- **Found during:** Task 1 (LiveDisplayManager)
- **Issue:** IRenderable type not found without explicit using for Spectre.Console.Rendering namespace
- **Fix:** Added `using Spectre.Console.Rendering;` to LiveDisplayManager.cs
- **Files modified:** src/AgentHub.Cli/Output/LiveDisplayManager.cs
- **Verification:** Build succeeded
- **Committed in:** c8bdbf9

**2. [Rule 3 - Blocking] C# yield-in-try-catch restriction in SseStreamReader**
- **Found during:** Task 1 (SseStreamReader)
- **Issue:** CS1626: Cannot yield a value in the body of a try block with a catch clause
- **Fix:** Restructured to use Channel<T> producer/consumer pattern -- producer runs in Task.Run with try-catch, consumer yields from channel reader
- **Files modified:** src/AgentHub.Cli/Api/SseStreamReader.cs
- **Verification:** Build succeeded, all 5 SSE tests pass
- **Committed in:** c8bdbf9

**3. [Rule 1 - Bug] NotificationService concurrent write corruption**
- **Found during:** Task 2 (NotificationServiceTests)
- **Issue:** FileShare.None on both read and write operations caused IOException when multiple threads wrote simultaneously
- **Fix:** Replaced file-level locking with in-process `lock` object for thread-safe read-modify-write
- **Files modified:** src/AgentHub.Cli/Notifications/NotificationService.cs
- **Verification:** ConcurrentWrites_DoNotCorruptFile test passes
- **Committed in:** 9ba127e

---

**Total deviations:** 3 auto-fixed (2 blocking, 1 bug)
**Impact on plan:** All auto-fixes necessary for correct compilation and thread safety. No scope creep.

## Issues Encountered
- Missing `using Xunit;` in SseStreamReaderTests.cs caused initial test build failure -- resolved by adding the import

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- CLI streaming and monitoring capabilities complete
- Ready for Plan 03 (interactive mode, shell completions, output theming) if applicable
- All 18 SSE/notification tests provide regression safety for future changes
- ApprovalPromptHandler ready for integration with session watch Live display

---
*Phase: 03-cli-client*
*Completed: 2026-03-08*
