---
phase: 10-multi-agent-coordination
plan: 01
subsystem: orchestration
tags: [placement-engine, weighted-scoring, cascade-limits, session-tracking, coordination]

# Dependency graph
requires:
  - phase: 07-host-inventory
    provides: HostMetricCache with real-time CPU/memory snapshots
  - phase: 07-host-inventory
    provides: SessionEntity.ParentSessionId column in schema
provides:
  - PlacementOptions with configurable CPU/memory/session weights
  - CoordinationOptions with cascade depth and child count limits
  - ActiveSessionTracker for per-host in-memory session counting
  - Weighted SimplePlacementEngine scoring (replaces first-match)
  - ValidateCascadeLimitsAsync for depth and child count enforcement
  - ChildSpawned/ChildCompleted/ChildFailed event kinds
  - ParentSessionId on StartSessionRequest and SessionSummary DTOs
affects: [10-02-spawn-api, 10-03-event-routing, 10-04-dashboard]

# Tech tracking
tech-stack:
  added: []
  patterns: [weighted-placement-scoring, cascade-limit-validation, active-session-tracking]

key-files:
  created:
    - src/AgentHub.Orchestration/Placement/PlacementOptions.cs
    - src/AgentHub.Orchestration/Coordinator/CoordinationOptions.cs
    - src/AgentHub.Orchestration/Coordinator/ActiveSessionTracker.cs
    - tests/AgentHub.Tests/PlacementScoringTests.cs
    - tests/AgentHub.Tests/CascadeLimitTests.cs
    - tests/AgentHub.Tests/ActiveSessionTrackerTests.cs
  modified:
    - src/AgentHub.Contracts/Models.cs
    - src/AgentHub.Orchestration/Placement/SimplePlacementEngine.cs
    - src/AgentHub.Orchestration/Coordinator/SessionCoordinator.cs
    - src/AgentHub.Service/Program.cs
    - src/AgentHub.Service/appsettings.json

key-decisions:
  - "ScoreNode returns -1 for exclusion (stale, no metrics, low disk, session cap)"
  - "ValidateCascadeLimitsAsync is static for testability; Plan 02 will wire into StartSessionAsync"
  - "Stale metric threshold: >60s excluded, >30s penalized with StaleMetricPenalty multiplier"

patterns-established:
  - "Weighted scoring pattern: CPU/Mem/Session factors with configurable weights via IOptions"
  - "Hard filter then score pattern: exclude ineligible nodes first, rank remainder"

requirements-completed: [COORD-04, COORD-05]

# Metrics
duration: 6min
completed: 2026-03-22
---

# Phase 10 Plan 01: Scoring and Safety Foundation Summary

**Weighted placement scoring with CPU/memory/session factors, cascade depth/count validation, and active session tracking for multi-agent coordination**

## Performance

- **Duration:** 6 min
- **Started:** 2026-03-22T10:52:27Z
- **Completed:** 2026-03-22T10:58:15Z
- **Tasks:** 2
- **Files modified:** 11

## Accomplishments
- Upgraded SimplePlacementEngine from first-match to weighted scoring using HostMetricCache data
- Created cascade limit validation with depth walking and child counting
- Added ActiveSessionTracker for thread-safe per-host session counting
- Extended contracts with ParentSessionId on DTOs and new event kinds
- 17 new tests (6 ActiveSessionTracker + 7 PlacementScoring + 4 CascadeLimit), all passing

## Task Commits

Each task was committed atomically:

1. **Task 1: Contracts, configuration options, and active session tracker** - `4d9f365` (feat)
2. **Task 2: Weighted placement scoring and cascade limit validation** - `9b452d3` (feat)

## Files Created/Modified
- `src/AgentHub.Contracts/Models.cs` - Added ChildSpawned/ChildCompleted/ChildFailed enum values, ParentSessionId on DTOs
- `src/AgentHub.Orchestration/Placement/PlacementOptions.cs` - IOptions config for placement weights and limits
- `src/AgentHub.Orchestration/Coordinator/CoordinationOptions.cs` - IOptions config for cascade depth/count limits
- `src/AgentHub.Orchestration/Coordinator/ActiveSessionTracker.cs` - ConcurrentDictionary-based per-host session counter
- `src/AgentHub.Orchestration/Placement/SimplePlacementEngine.cs` - Weighted scoring with ScoreNode method
- `src/AgentHub.Orchestration/Coordinator/SessionCoordinator.cs` - ValidateCascadeLimitsAsync static method
- `src/AgentHub.Service/Program.cs` - DI registration for options and tracker
- `src/AgentHub.Service/appsettings.json` - Placement and Coordination config sections
- `tests/AgentHub.Tests/ActiveSessionTrackerTests.cs` - 6 unit tests for tracker behavior
- `tests/AgentHub.Tests/PlacementScoringTests.cs` - 7 unit tests for weighted scoring
- `tests/AgentHub.Tests/CascadeLimitTests.cs` - 4 unit tests for cascade validation

## Decisions Made
- ScoreNode returns -1 to exclude hosts (no metrics, stale >60s, low disk, at session cap)
- ValidateCascadeLimitsAsync kept as static method for testability; Plan 02 will wire it into the spawn flow
- Stale metric threshold: >60s fully excluded, >30s penalized with configurable StaleMetricPenalty multiplier
- TargetHostId bypass preserved before scoring logic (returns immediately if match found)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed MockSshClient missing StartStreamingCommandAsync**
- **Found during:** Task 1
- **Issue:** MockSshHostConnection did not implement StartStreamingCommandAsync added to ISshHostConnection interface, preventing test project compilation
- **Fix:** Added stub implementation returning Task.CompletedTask
- **Files modified:** tests/AgentHub.Tests/Helpers/MockSshClient.cs
- **Verification:** Test project compiles, all related tests pass
- **Committed in:** 4d9f365 (Task 1 commit)

**2. [Rule 3 - Blocking] Updated existing test constructors for new SessionCoordinator parameter**
- **Found during:** Task 2
- **Issue:** SessionCoordinatorTests, SessionCoordinatorApprovalTests, and SteeringTests constructed SessionCoordinator without the new IOptions<CoordinationOptions> parameter
- **Fix:** Added Options.Create(new CoordinationOptions()) as last parameter to all three test files
- **Files modified:** tests/AgentHub.Tests/SessionCoordinatorTests.cs, SessionCoordinatorApprovalTests.cs, SteeringTests.cs
- **Verification:** All 308 existing tests continue to pass
- **Committed in:** 9b452d3 (Task 2 commit)

---

**Total deviations:** 2 auto-fixed (2 blocking)
**Impact on plan:** Both fixes necessary for compilation. No scope creep.

## Issues Encountered
- 2 pre-existing test failures in SshBackendWorktreeTests (unrelated to this plan's changes, exposed by MockSshClient interface fix)

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Placement scoring foundation ready for spawn API (Plan 02)
- ValidateCascadeLimitsAsync ready to wire into StartSessionAsync spawn flow
- ActiveSessionTracker ready for increment/decrement on session start/stop
- New event kinds ready for child lifecycle event emission

---
*Phase: 10-multi-agent-coordination*
*Completed: 2026-03-22*
