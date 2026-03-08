---
phase: 02-session-orchestration-and-agent-execution
plan: 02
subsystem: orchestration
tags: [ssh.net, ssh-backend, heartbeat, orphan-detection, session-monitor, two-phase-stop]

requires:
  - phase: 02-session-orchestration-and-agent-execution
    provides: "SessionEntity Phase 2 fields, ForceKill protocol command, StartSessionRequest.IsFireAndForget"
  - phase: 01-foundation-and-event-infrastructure
    provides: "ISessionBackend interface, HostCommandProtocol, AgentHubDbContext, EntityMappers"
provides:
  - "Real SSH execution backend (SshBackend) using SSH.NET via ISshHostConnectionFactory"
  - "SshHostConnection wrapper with heartbeat, connect, execute, dispose"
  - "ISshHostConnection interface for testability"
  - "Two-phase stop: SIGINT (graceful) then SIGTERM (force-kill) after configurable grace period"
  - "SessionMonitorService BackgroundService for heartbeat/orphan detection (90s timeout)"
  - "MockSshHostConnection and MockSshHostConnectionFactory for unit testing"
  - "ISessionBackend.StopAsync with forceKill parameter"
  - "DB-backed session state (no more ConcurrentDictionary)"
affects: [02-03, 02-04]

tech-stack:
  added: []
  patterns: [ssh-host-connection-factory, two-phase-stop, heartbeat-orphan-detection, db-backed-session-state]

key-files:
  created:
    - src/AgentHub.Orchestration/Backends/SshHostConnection.cs
    - src/AgentHub.Orchestration/Monitoring/SessionMonitorService.cs
    - tests/AgentHub.Tests/SshBackendTests.cs
    - tests/AgentHub.Tests/Helpers/MockSshClient.cs
  modified:
    - src/AgentHub.Orchestration/Backends/SshBackend.cs
    - src/AgentHub.Orchestration/Abstractions.cs
    - src/AgentHub.Orchestration/Backends/InMemoryBackend.cs
    - src/AgentHub.Orchestration/Backends/NomadBackend.cs
    - src/AgentHub.Orchestration/Coordinator/SessionCoordinator.cs

key-decisions:
  - "ISshHostConnection interface extracted for DI and testability (not tied to SSH.NET types)"
  - "ISshHostConnectionFactory pattern enables mock injection in tests without real SSH"
  - "SessionMonitorService.RunOnceAsync() exposed as public for test-controlled monitoring cycles"
  - "Grace period for two-phase stop configurable via Ssh:GracePeriodSeconds (default 10s)"

patterns-established:
  - "ISshHostConnectionFactory: Factory pattern for SSH connections enabling DI and test mocking"
  - "SessionMonitorService.RunOnceAsync: Public single-cycle method for testable BackgroundService"
  - "TestDbContextFactory: File-scoped IDbContextFactory implementation for InMemory DB tests"
  - "Two-phase stop: StopAsync(sessionId, forceKill) with SIGINT->delay->SIGTERM pattern"

requirements-completed: [SESS-01, SESS-02, SESS-03, SESS-04]

duration: 7min
completed: 2026-03-08
---

# Phase 2 Plan 2: SSH Backend and Session Monitor Summary

**Real SSH execution backend with SSH.NET, two-phase stop (SIGINT/SIGTERM), DB-backed session state, and heartbeat-based orphan detection via SessionMonitorService**

## Performance

- **Duration:** 7 min
- **Started:** 2026-03-08T15:16:01Z
- **Completed:** 2026-03-08T15:23:00Z
- **Tasks:** 2
- **Files modified:** 9

## Accomplishments
- Rewrote SshBackend from ConcurrentDictionary stub to real SSH.NET execution with DB persistence
- Implemented two-phase stop mechanism: SIGINT (graceful) with configurable grace period, then SIGTERM (force-kill)
- Built SessionMonitorService BackgroundService that detects orphaned sessions after 90s heartbeat timeout
- Created ISshHostConnection/ISshHostConnectionFactory abstraction for testability without real SSH connections
- Added forceKill parameter to ISessionBackend.StopAsync across all backends
- All 142 tests pass (11 new SshBackend/Monitor tests + 131 existing)

## Task Commits

Each task was committed atomically:

1. **Task 1: SshHostConnection wrapper and SshBackend rewrite** - `ae68618` (feat)
2. **Task 2 RED: Failing tests for SshBackend and SessionMonitorService** - `b620383` (test)
3. **Task 2 GREEN: SessionMonitorService implementation** - `83be749` (feat)

_TDD approach for Task 2: tests written first (RED), then implementation to pass them (GREEN)._

## Files Created/Modified
- `src/AgentHub.Orchestration/Backends/SshHostConnection.cs` - ISshHostConnection interface, SshHostConnection with SSH.NET heartbeat, ISshHostConnectionFactory
- `src/AgentHub.Orchestration/Backends/SshBackend.cs` - Complete rewrite: SSH.NET execution, DB persistence, two-phase stop, event streaming
- `src/AgentHub.Orchestration/Monitoring/SessionMonitorService.cs` - BackgroundService scanning for orphaned sessions via heartbeat timeout
- `src/AgentHub.Orchestration/Abstractions.cs` - ISessionBackend.StopAsync updated with forceKill parameter
- `src/AgentHub.Orchestration/Backends/InMemoryBackend.cs` - Updated StopAsync signature
- `src/AgentHub.Orchestration/Backends/NomadBackend.cs` - Updated StopAsync signature
- `src/AgentHub.Orchestration/Coordinator/SessionCoordinator.cs` - Added forceKill overload for StopSessionAsync
- `tests/AgentHub.Tests/SshBackendTests.cs` - 11 tests covering start, stop, list, get, and monitor operations
- `tests/AgentHub.Tests/Helpers/MockSshClient.cs` - MockSshHostConnection and MockSshHostConnectionFactory

## Decisions Made
- Extracted ISshHostConnection interface rather than mocking SSH.NET types directly -- cleaner test boundary
- ISshHostConnectionFactory injected via DI, enabling mock substitution without service locator pattern
- SessionMonitorService exposes RunOnceAsync() publicly for deterministic test control (no timer races in tests)
- Grace period defaults to 10s, configurable via IConfiguration for different deployment environments
- Used IConfiguration string parsing (not GetValue<T>) to avoid Microsoft.Extensions.Configuration.Binder dependency

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Avoided Configuration.GetValue<T> due to missing binder package**
- **Found during:** Task 1 (SshBackend constructor)
- **Issue:** IConfiguration.GetValue<int?> extension requires Microsoft.Extensions.Configuration.Binder which was not referenced
- **Fix:** Used int.TryParse on configuration string value instead of GetValue<T>
- **Files modified:** src/AgentHub.Orchestration/Backends/SshBackend.cs
- **Verification:** Build succeeds without additional package dependency

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** Minor -- same functionality achieved without additional NuGet dependency.

## Issues Encountered
None.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- SshBackend ready for real SSH connections (requires SSH keys on deployment hosts)
- SessionMonitorService ready for DI registration in Program.cs (Plan 02-04)
- ISshHostConnectionFactory ready for DI registration
- ForceKill parameter wired through entire backend chain
- Approval flow (Plan 02-03) can build on the session state management patterns established here

---
*Phase: 02-session-orchestration-and-agent-execution*
*Completed: 2026-03-08*

## Self-Check: PASSED

- All 6 key files exist on disk
- All 3 task commits verified in git log
- Line count minimums met: SshBackend (342/120), SshHostConnection (121/60), SessionMonitorService (112/50), SshBackendTests (426/80)
- 142 total tests pass (11 new + 131 existing)
