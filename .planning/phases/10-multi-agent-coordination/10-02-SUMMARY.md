---
phase: 10-multi-agent-coordination
plan: 02
subsystem: orchestration
tags: [spawn-api, parent-child-sessions, event-forwarding, ssh-intercept, cascade-limits, sse]

# Dependency graph
requires:
  - phase: 10-multi-agent-coordination
    provides: PlacementOptions, CoordinationOptions, ActiveSessionTracker, ValidateCascadeLimitsAsync, ChildSpawned/Completed/Failed event kinds, ParentSessionId on DTOs
provides:
  - Spawn API wiring: StartSessionAsync validates cascade limits and sets ParentSessionId FK
  - ChildSpawned event emitted to parent stream on child creation
  - Orphaned children warning on parent stop
  - ActiveSessionTracker increment/decrement on session start/stop
  - GET /api/sessions?parentId= filter for listing children
  - Child-to-parent event forwarding via DurableEventService parent cache
  - BroadcastToParent with lifecycle/includeChildren filtering
  - SSE endpoint ?includeChildren=true query parameter
  - SpawnInterceptor static parser for ##AGENTHUB_SPAWN:{json}## SSH stdout markers
  - SSH stdout spawn intercept in SshBackend PTY reader
affects: [10-03-dashboard, 10-04-integration]

# Tech tracking
tech-stack:
  added: []
  patterns: [parent-child-event-forwarding, spawn-marker-intercept, lazy-di-resolution]

key-files:
  created:
    - src/AgentHub.Orchestration/Coordinator/SpawnInterceptor.cs
    - tests/AgentHub.Tests/SpawnSessionTests.cs
    - tests/AgentHub.Tests/ChildEventForwardingTests.cs
    - tests/AgentHub.Tests/Helpers/NullServiceProvider.cs
  modified:
    - src/AgentHub.Orchestration/Coordinator/SessionCoordinator.cs
    - src/AgentHub.Orchestration/Events/DurableEventService.cs
    - src/AgentHub.Orchestration/Events/SseSubscriptionManager.cs
    - src/AgentHub.Orchestration/Backends/SshBackend.cs
    - src/AgentHub.Orchestration/Data/EntityMappers.cs
    - src/AgentHub.Service/Program.cs

key-decisions:
  - "IServiceProvider injected into SshBackend to lazily resolve ISessionCoordinator (breaks circular DI)"
  - "Parent cache in DurableEventService as ConcurrentDictionary with lazy DB lookup and manual invalidation"
  - "Orphaned children warning persisted directly to DB events (StopSessionAsync has no emit callback)"
  - "SpawnTrackingBackend in tests persists to InMemory DB for cascade validation to work"
  - "SubscriberInfo wrapper record with IncludeChildren flag for selective event forwarding"

patterns-established:
  - "Parent forwarding pattern: emit to child stream, then check parent cache and forward with [child-id] prefix"
  - "Loop prevention via forwarded=true meta tag on forwarded events"
  - "Fire-and-forget spawn in PTY reader to avoid blocking output streaming"

requirements-completed: [COORD-01, COORD-02, COORD-03]

# Metrics
duration: 10min
completed: 2026-03-22
---

# Phase 10 Plan 02: Spawn API and Event Forwarding Summary

**Complete spawn flow with parent-child FK persistence, cascade validation, lifecycle event forwarding to parent SSE streams, and SSH stdout spawn marker interception**

## Performance

- **Duration:** 10 min
- **Started:** 2026-03-22T11:01:23Z
- **Completed:** 2026-03-22T11:11:00Z
- **Tasks:** 3
- **Files modified:** 14

## Accomplishments
- Wired spawn API: StartSessionAsync validates cascade limits, sets ParentSessionId FK, emits ChildSpawned to parent
- Built child-to-parent event forwarding with lifecycle/all-event modes and loop prevention
- Created SpawnInterceptor for SSH stdout ##AGENTHUB_SPAWN:{json}## markers with fire-and-forget child creation
- 13 new tests (7 SpawnSession + 6 ChildEventForwarding), all passing alongside 17 existing Phase 10 tests

## Task Commits

Each task was committed atomically:

1. **Task 1: Spawn API wiring and parent-child persistence** - `b941744` (feat)
2. **Task 2: Child-to-parent event forwarding and SSE stream changes** - `62ab79d` (feat)
3. **Task 3: SSH stdout spawn intercept** - `bf6edcb` (feat)

## Files Created/Modified
- `src/AgentHub.Orchestration/Coordinator/SessionCoordinator.cs` - Cascade validation in StartSessionAsync, orphan warning in StopSessionAsync, ParentSessionId in history
- `src/AgentHub.Orchestration/Events/DurableEventService.cs` - Parent cache, ForwardToParentAsync, InvalidateParentCache, includeChildren on SubscribeSession
- `src/AgentHub.Orchestration/Events/SseSubscriptionManager.cs` - SubscriberInfo wrapper, BroadcastToParent with lifecycle filtering
- `src/AgentHub.Orchestration/Coordinator/SpawnInterceptor.cs` - Static spawn marker parser with GeneratedRegex
- `src/AgentHub.Orchestration/Backends/SshBackend.cs` - Spawn intercept in ReadAgentOutputAsync, IServiceProvider for lazy coordinator resolution
- `src/AgentHub.Orchestration/Data/EntityMappers.cs` - ParentSessionId in SessionEntity.ToDto()
- `src/AgentHub.Service/Program.cs` - ?parentId filter on GET /api/sessions, ?includeChildren on SSE endpoint
- `tests/AgentHub.Tests/SpawnSessionTests.cs` - 7 spawn tests (FK, events, limits, orphans, tracker, history)
- `tests/AgentHub.Tests/ChildEventForwardingTests.cs` - 6 forwarding tests (lifecycle, includeChildren, prefix, meta, loop, cache)
- `tests/AgentHub.Tests/Helpers/NullServiceProvider.cs` - Test helper for SshBackend construction

## Decisions Made
- Used IServiceProvider.GetRequiredService to lazily resolve ISessionCoordinator in SshBackend, breaking circular DI dependency
- Parent cache as ConcurrentDictionary<string, string?> with manual InvalidateParentCache -- avoids per-event DB queries
- Orphaned children warning persisted directly to DB events since StopSessionAsync has no emit callback parameter
- SpawnTrackingBackend in tests persists SessionEntity to InMemory DB so cascade validation queries work correctly
- SubscriberInfo record wraps ChannelWriter with IncludeChildren flag for selective forwarding

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Updated existing test constructors for new SessionCoordinator parameter**
- **Found during:** Task 1
- **Issue:** SessionCoordinatorTests, SessionCoordinatorApprovalTests, and SteeringTests constructed SessionCoordinator without the new ActiveSessionTracker parameter
- **Fix:** Added `new ActiveSessionTracker()` as last parameter to all three test files
- **Committed in:** b941744 (Task 1 commit)

**2. [Rule 3 - Blocking] Updated SshBackend test constructors for new IServiceProvider parameter**
- **Found during:** Task 3
- **Issue:** SshBackendTests and SshBackendWorktreeTests constructed SshBackend without the new IServiceProvider parameter
- **Fix:** Created NullServiceProvider test helper, added as parameter to both test files
- **Committed in:** bf6edcb (Task 3 commit)

**3. [Rule 1 - Bug] SpawnTrackingBackend needed DB persistence for cascade validation**
- **Found during:** Task 1
- **Issue:** Test backend didn't persist SessionEntity to InMemory DB, causing ValidateCascadeLimitsAsync to find no sessions
- **Fix:** Added SetDbFactory method and DB persistence in StartAsync/StopAsync
- **Committed in:** b941744 (Task 1 commit)

---

**Total deviations:** 3 auto-fixed (2 blocking, 1 bug)
**Impact on plan:** All fixes necessary for compilation and test correctness. No scope creep.

## Issues Encountered
- 1 pre-existing test failure in SshBackendWorktreeTests.ForceKill (unrelated to this plan's changes, noted in 10-01 summary)

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Spawn flow fully operational: API, FK persistence, cascade limits, event forwarding, SSH intercept
- Ready for dashboard UI integration (Phase 10-03/04)
- Parent cache invalidation wired but should be called from session completion paths
- includeChildren=true enables full child output streaming for dashboard views

## Self-Check: PASSED

All 5 created files verified on disk. All 3 task commits verified in git log.

---
*Phase: 10-multi-agent-coordination*
*Completed: 2026-03-22*
