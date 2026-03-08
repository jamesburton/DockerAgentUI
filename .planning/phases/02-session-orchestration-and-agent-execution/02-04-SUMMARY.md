---
phase: 02-session-orchestration-and-agent-execution
plan: 04
subsystem: orchestration
tags: [di-wiring, api-endpoints, pagination, force-kill, approval-resolution, session-history]

requires:
  - phase: 02-session-orchestration-and-agent-execution
    provides: "SessionCoordinator, SshBackend with forceKill, ApprovalService, SessionMonitorService, ConfigLoader, ConfigScopeMerger"
  - phase: 01-foundation-and-event-infrastructure
    provides: "DurableEventService, AgentHubDbContext, ISessionBackend, InMemoryBackend, SseSubscriptionManager"
provides:
  - "SessionCoordinator with forceKill routing, prompt resolution, and paginated session history"
  - "GET /api/sessions with pagination (skip/take) and state filtering"
  - "GET /api/sessions/{id}/history for full event replay"
  - "DELETE /api/sessions/{id}?force=true for force-kill, without force for graceful stop"
  - "POST /api/approvals/{id}/resolve for approval resolution"
  - "All Phase 2 services registered in DI: ApprovalService, ConfigLoader, ConfigScopeMerger, SessionMonitorService, ISshHostConnectionFactory"
affects: [03-cli, 04-dashboard]

tech-stack:
  added: []
  patterns: [paginated-api-response, in-memory-sort-for-sqlite, approval-resolution-endpoint]

key-files:
  created:
    - tests/AgentHub.Tests/SessionCoordinatorTests.cs
    - tests/AgentHub.Tests/SessionHistoryTests.cs
  modified:
    - src/AgentHub.Orchestration/Coordinator/SessionCoordinator.cs
    - src/AgentHub.Orchestration/Abstractions.cs
    - src/AgentHub.Service/Program.cs
    - tests/AgentHub.Tests/ApiEndpointTests.cs

key-decisions:
  - "In-memory sorting for paginated session queries to avoid SQLite DateTimeOffset ORDER BY limitation"
  - "GET /api/sessions returns {items, totalCount} object instead of plain array for pagination metadata"
  - "ISshHostConnectionFactory registered in DI (was missing from Plan 02-02 wiring)"
  - "SessionMonitorService registered via factory lambda (optional TimeSpan? params not DI-resolvable)"

patterns-established:
  - "Paginated API response: {items: [], totalCount: int} for list endpoints with skip/take"
  - "Approval resolution: POST /api/approvals/{id}/resolve with {approved, resolvedBy}"
  - "Force-kill via DELETE with ?force=true query parameter"
  - "In-memory sort pattern for SQLite DateTimeOffset columns"

requirements-completed: [SESS-03, SESS-05]

duration: 7min
completed: 2026-03-08
---

# Phase 2 Plan 4: Integration Wiring, API Endpoints, and Session History Summary

**Wired all Phase 2 services in DI, added paginated session history with state filtering, force-kill via DELETE, and approval resolution endpoint with full integration tests**

## Performance

- **Duration:** 7 min
- **Started:** 2026-03-08T15:26:03Z
- **Completed:** 2026-03-08T15:34:01Z
- **Tasks:** 2
- **Files modified:** 6

## Accomplishments
- Updated SessionCoordinator with ApprovalService and IDbContextFactory injection, forceKill routing, and prompt resolution (Prompt -> Reason -> empty fallback)
- Added GetSessionHistoryAsync with pagination and optional state filtering via DB query
- Registered all Phase 2 services in DI: ApprovalService, ConfigLoader, ConfigScopeMerger, SessionMonitorService, ISshHostConnectionFactory
- Added 5 new API endpoints: paginated sessions list, session history replay, DELETE force-kill, approval resolution
- All 165 tests pass (15 new + 150 existing)

## Task Commits

Each task was committed atomically:

1. **Task 1: Update SessionCoordinator with force-kill and approval integration** - `64a11d2` (feat)
2. **Task 2: DI wiring, API endpoints, and session history tests** - `dfafa36` (feat)

_TDD approach: tests written alongside implementation._

## Files Created/Modified
- `src/AgentHub.Orchestration/Abstractions.cs` - Added forceKill overload and GetSessionHistoryAsync to ISessionCoordinator
- `src/AgentHub.Orchestration/Coordinator/SessionCoordinator.cs` - ApprovalService/IDbContextFactory injection, prompt resolution, paginated history query
- `src/AgentHub.Service/Program.cs` - All Phase 2 DI registrations, 5 new API endpoints, ApprovalResolveRequest record
- `tests/AgentHub.Tests/SessionCoordinatorTests.cs` - 7 unit tests: forceKill routing, prompt fallback, pagination, state filter
- `tests/AgentHub.Tests/SessionHistoryTests.cs` - 8 integration tests: pagination, state filter, history replay, force-kill, approval resolve
- `tests/AgentHub.Tests/ApiEndpointTests.cs` - Updated for new paginated response format

## Decisions Made
- Used in-memory sorting for session pagination queries because SQLite's EF Core provider does not support DateTimeOffset in ORDER BY clauses. Acceptable for user-scoped queries.
- Changed GET /api/sessions response from plain array to `{items, totalCount}` object to support pagination metadata.
- Registered ISshHostConnectionFactory in DI (was missing from Program.cs despite being created in Plan 02-02).
- Registered SessionMonitorService via manual factory lambda because its constructor has optional `TimeSpan?` parameters that DI cannot auto-resolve.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Missing ISshHostConnectionFactory DI registration**
- **Found during:** Task 2 (integration tests failing with DI validation error)
- **Issue:** ISshHostConnectionFactory was not registered in Program.cs despite SshBackend depending on it
- **Fix:** Added `builder.Services.AddSingleton<ISshHostConnectionFactory, SshHostConnectionFactory>()`
- **Files modified:** src/AgentHub.Service/Program.cs
- **Verification:** All integration tests pass
- **Committed in:** dfafa36 (Task 2 commit)

**2. [Rule 3 - Blocking] SessionMonitorService DI resolution failure**
- **Found during:** Task 2 (integration tests failing)
- **Issue:** `AddHostedService<SessionMonitorService>()` fails because DI cannot resolve optional `TimeSpan?` constructor parameters
- **Fix:** Changed to manual factory registration with `AddSingleton<IHostedService>(sp => new SessionMonitorService(...))`
- **Files modified:** src/AgentHub.Service/Program.cs
- **Verification:** DI resolves successfully, tests pass
- **Committed in:** dfafa36 (Task 2 commit)

**3. [Rule 1 - Bug] SQLite DateTimeOffset ORDER BY limitation**
- **Found during:** Task 2 (GET /api/sessions returning 500)
- **Issue:** SQLite does not support expressions of type 'DateTimeOffset' in ORDER BY clauses via EF Core
- **Fix:** Fetch all matching entities then sort/paginate in memory (acceptable for user-scoped queries)
- **Files modified:** src/AgentHub.Orchestration/Coordinator/SessionCoordinator.cs, src/AgentHub.Service/Program.cs
- **Verification:** Pagination and history endpoints return correct results
- **Committed in:** dfafa36 (Task 2 commit)

**4. [Rule 1 - Bug] Updated ApiEndpointTests for new response format**
- **Found during:** Task 2 (pre-existing test breaking)
- **Issue:** GetSessions_Returns200WithEmptyListInitially expected plain List<SessionSummary> but endpoint now returns {items, totalCount}
- **Fix:** Updated test to deserialize JsonElement and check items/totalCount properties
- **Files modified:** tests/AgentHub.Tests/ApiEndpointTests.cs
- **Verification:** All 165 tests pass
- **Committed in:** dfafa36 (Task 2 commit)

---

**Total deviations:** 4 auto-fixed (2 blocking, 2 bugs)
**Impact on plan:** All fixes necessary for correctness and DI resolution. No scope creep.

## Issues Encountered
None beyond the auto-fixed deviations above.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- All Phase 2 services fully wired and running in DI
- API endpoints ready for Phase 3 CLI consumption: paginated sessions, force-kill, approval resolution
- Session history replay endpoint ready for Phase 4 dashboard
- 165 tests passing (no regressions from Phase 1)

---
*Phase: 02-session-orchestration-and-agent-execution*
*Completed: 2026-03-08*
