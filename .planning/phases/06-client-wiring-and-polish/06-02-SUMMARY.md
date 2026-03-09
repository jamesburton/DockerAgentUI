---
phase: 06-client-wiring-and-polish
plan: 02
subsystem: monitoring
tags: [ssh, metrics, background-service, sse, cpu, memory]

requires:
  - phase: 02-orchestration
    provides: ISshHostConnectionFactory, DurableEventService, HostEntity, EntityMappers
provides:
  - HostMetricPollingService BackgroundService for SSH-based host metric collection
  - HostMetrics SSE event kind for real-time dashboard delivery
  - HostEntity metric columns (CpuPercent, MemUsedMb, MemTotalMb, MetricsUpdatedUtc)
  - HostStatusReport MemoryTotalMb field
affects: [06-03, dashboard, web-ui]

tech-stack:
  added: []
  patterns: [BackgroundService with RunOnceAsync for testability, pipe-delimited SSH metric output parsing]

key-files:
  created:
    - src/AgentHub.Orchestration/Monitoring/HostMetricPollingService.cs
    - tests/AgentHub.Tests/HostMetricTests.cs
  modified:
    - src/AgentHub.Contracts/Models.cs
    - src/AgentHub.Orchestration/Data/Entities/HostEntity.cs
    - src/AgentHub.Orchestration/Data/EntityMappers.cs
    - src/AgentHub.Orchestration/HostDaemon/HostDaemonModels.cs
    - src/AgentHub.Service/Program.cs
    - tests/AgentHub.Tests/AgentHub.Tests.csproj

key-decisions:
  - "ISshHostConnectionFactory uses host/username/privateKeyPath from IConfiguration (same pattern as SshBackend)"
  - "Pipe-delimited metric output format: cpu|memTotal|memUsed for all OS commands"
  - "Static ParseMetricOutput and GetMetricCommand methods for unit testability without SSH mocking"

patterns-established:
  - "BackgroundService metric polling: RunOnceAsync exposed for testing, per-host try/catch for fault isolation"
  - "OS detection via string.Contains case-insensitive on host.Os field"

requirements-completed: [MON-02]

duration: 9min
completed: 2026-03-09
---

# Phase 6 Plan 2: Host Metric Collection Summary

**SSH-based host metric polling service with OS-aware CPU/memory collection, HostEntity persistence, and HostMetrics SSE events**

## Performance

- **Duration:** 9 min
- **Started:** 2026-03-09T14:59:41Z
- **Completed:** 2026-03-09T15:08:52Z
- **Tasks:** 2
- **Files modified:** 8

## Accomplishments
- HostMetricPollingService polls enabled hosts via SSH every 30 seconds with OS-appropriate commands (Windows/Linux/macOS)
- HostEntity extended with CpuPercent, MemUsedMb, MemTotalMb, MetricsUpdatedUtc columns; EntityMappers.ToDto passes values to HostRecord
- HostMetrics SSE event kind emitted after each poll with hostId, cpu, memUsedMb, memTotalMb in meta
- HostStatusReport extended with MemoryTotalMb field
- 13 unit tests for metric parsing, command generation, DTO mapping, and null handling

## Task Commits

Each task was committed atomically:

1. **Task 1: Extend data model and create HostMetricPollingService** - `c054887` (feat)
2. **Task 2: Register HostMetricPollingService in DI** - `7972a13` (feat)

_Note: Data model changes (HostEntity fields, HostMetrics enum, EntityMappers, HostDaemonModels, HostMetricPollingService) were included in a prior 06-01 commit. Task 1 commit enables the previously-excluded HostMetricTests._

## Files Created/Modified
- `src/AgentHub.Orchestration/Monitoring/HostMetricPollingService.cs` - BackgroundService: SSH metric collection with OS-aware commands, pipe-delimited parsing, DB persistence, SSE emission
- `src/AgentHub.Contracts/Models.cs` - Added HostMetrics to SessionEventKind enum
- `src/AgentHub.Orchestration/Data/Entities/HostEntity.cs` - Added CpuPercent, MemUsedMb, MemTotalMb, MetricsUpdatedUtc properties
- `src/AgentHub.Orchestration/Data/EntityMappers.cs` - Updated ToDto to pass metric fields to HostRecord
- `src/AgentHub.Orchestration/HostDaemon/HostDaemonModels.cs` - Added MemoryTotalMb to HostStatusReport
- `src/AgentHub.Service/Program.cs` - Registered HostMetricPollingService as AddHostedService
- `tests/AgentHub.Tests/HostMetricTests.cs` - 13 tests for parsing, commands, mapping, null handling
- `tests/AgentHub.Tests/AgentHub.Tests.csproj` - Removed Compile exclusion for HostMetricTests.cs

## Decisions Made
- ISshHostConnectionFactory uses host/username/privateKeyPath from IConfiguration (consistent with SshBackend pattern)
- Pipe-delimited output format (cpu|memTotal|memUsed) for all OS metric commands enables simple, reliable parsing
- Static ParseMetricOutput and GetMetricCommand methods allow unit testing without SSH infrastructure mocking

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Data model changes already committed by prior 06-01 execution**
- **Found during:** Task 1
- **Issue:** HostEntity metric fields, HostMetrics enum, EntityMappers mapping, HostDaemonModels, and HostMetricPollingService were already committed in the 06-01 commit (5c31402). HostMetricTests.cs was excluded from compilation.
- **Fix:** Verified all implementations match plan specification; removed Compile exclusion to enable tests
- **Files modified:** tests/AgentHub.Tests/AgentHub.Tests.csproj
- **Verification:** All 13 HostMetricTests pass
- **Committed in:** c054887

---

**Total deviations:** 1 auto-fixed (blocking - prior commit overlap)
**Impact on plan:** No scope change. All planned functionality delivered as specified.

## Issues Encountered
- Pre-existing integration test failures (23 tests in SessionHistoryTests, SseStreamingTests, ApiEndpointTests, NotificationServiceTests, OutputFormatterTests) are unrelated to this plan's changes. These failures existed before and are out of scope.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Host metric infrastructure complete and ready for dashboard consumption
- GET /api/hosts now returns CpuPercent, MemUsedMb, MemTotalMb once polling runs
- HostMetrics SSE events available on /api/events for real-time updates
- Plan 06-03 can wire dashboard components to display live host metrics

---
*Phase: 06-client-wiring-and-polish*
*Completed: 2026-03-09*
