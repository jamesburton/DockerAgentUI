---
phase: 01-foundation-and-event-infrastructure
plan: 02
subsystem: event-streaming
tags: [sse, server-sent-events, durable-events, last-event-id, replay, channels, ef-core]

requires:
  - phase: 01-01
    provides: EF Core DbContext with Sessions and Events DbSets, SessionEventEntity with auto-increment Id, EntityMappers

provides:
  - DurableEventService replacing in-memory SessionEventBus
  - SseSubscriptionManager with ConcurrentDictionary-based subscriber lifecycle
  - Per-session SSE endpoint with Last-Event-ID replay at /api/sessions/{id}/events
  - Fleet-wide SSE endpoint with Last-Event-ID replay at /api/events
  - 8 unit tests for DurableEventService
  - 5 integration tests for SSE streaming endpoints

affects: [02-ssh-backend, 04-dashboard, fleet-monitoring]

tech-stack:
  added: [System.Net.ServerSentEvents (SseItem<T>)]
  patterns: [IDbContextFactory for singleton event service, ConcurrentDictionary subscriber management, SseItem<T> with EventId for replay, Channel-based live event streaming]

key-files:
  created:
    - src/AgentHub.Orchestration/Events/DurableEventService.cs
    - src/AgentHub.Orchestration/Events/SseSubscriptionManager.cs
    - tests/AgentHub.Tests/DurableEventServiceTests.cs
    - tests/AgentHub.Tests/SseStreamingTests.cs
  modified:
    - src/AgentHub.Service/Program.cs

key-decisions:
  - "ConcurrentDictionary keyed by Guid connectionId instead of ConcurrentBag for reliable subscriber removal"
  - "Singleton DurableEventService using IDbContextFactory for scoped DB access per operation"
  - "SseItem<T> EventId set to DB auto-increment Id for deterministic replay"

patterns-established:
  - "Durable event pattern: persist to DB first, then broadcast to live subscribers with DB-assigned ID"
  - "SSE replay pattern: parse Last-Event-ID header, query DB for events with Id > afterId, yield replayed events before live channel"
  - "Subscriber cleanup: ConcurrentDictionary with Guid keys allows reliable removal in finally block on cancellation"

requirements-completed: [MON-01, INFRA-01]

duration: 11min
completed: 2026-03-08
---

# Phase 1 Plan 02: Durable Event Streaming Summary

**DurableEventService with DB persistence, SseSubscriptionManager for subscriber lifecycle, per-session and fleet-wide SSE endpoints with Last-Event-ID replay support, and 13 passing tests**

## Performance

- **Duration:** 11 min
- **Started:** 2026-03-08T10:46:59Z
- **Completed:** 2026-03-08T10:58:23Z
- **Tasks:** 2
- **Files modified:** 5

## Accomplishments
- DurableEventService replaces the in-memory SessionEventBus with full DB persistence via IDbContextFactory
- SseSubscriptionManager manages per-session and fleet-wide subscriber sets with ConcurrentDictionary for reliable cleanup
- SSE endpoints support Last-Event-ID header for replaying missed events after client reconnection
- Fleet-wide endpoint at /api/events enables the Phase 4 dashboard to stream events from all sessions
- 8 unit tests covering persistence, broadcast, replay, and subscriber cleanup
- 5 integration tests covering SSE content type, replay correctness, fleet streaming, and 404 handling

## Task Commits

Each task was committed atomically:

1. **Task 1: DurableEventService with persistence, broadcast, and replay** - `457ddc7` (feat)
2. **Task 2: Wire SSE endpoints with Last-Event-ID and integration tests** - `c942d14` (feat)

## Files Created/Modified
- `src/AgentHub.Orchestration/Events/DurableEventService.cs` - Durable event bus: persists to DB, broadcasts to live SSE subscribers, replays on reconnect
- `src/AgentHub.Orchestration/Events/SseSubscriptionManager.cs` - Manages SSE subscriber lifecycle with ConcurrentDictionary for reliable removal
- `src/AgentHub.Service/Program.cs` - Replaced SessionEventBus with DurableEventService, added /api/events fleet endpoint, added Last-Event-ID support
- `tests/AgentHub.Tests/DurableEventServiceTests.cs` - 8 unit tests for event persistence, broadcast, replay, and cleanup
- `tests/AgentHub.Tests/SseStreamingTests.cs` - 5 integration tests for SSE endpoints with WebApplicationFactory

## Decisions Made
- Used ConcurrentDictionary<Guid, ChannelWriter> instead of ConcurrentBag for subscriber management -- enables reliable removal when clients disconnect, preventing memory leaks
- Registered DurableEventService as singleton with IDbContextFactory for scoped DB access -- avoids DbContext lifetime issues in long-running SSE connections
- Set SseItem EventId to DB auto-increment Id (as string) -- provides deterministic, monotonically increasing IDs for Last-Event-ID replay

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed FromHeader attribute namespace**
- **Found during:** Task 2 (SSE endpoint wiring)
- **Issue:** Initial `[FromHeaderAttribute]` used wrong namespace (`Microsoft.AspNetCore.Http` instead of `Microsoft.AspNetCore.Mvc`)
- **Fix:** Changed to `[Microsoft.AspNetCore.Mvc.FromHeader(Name = "Last-Event-ID")]`
- **Files modified:** src/AgentHub.Service/Program.cs
- **Verification:** Build succeeded
- **Committed in:** c942d14 (Task 2 commit)

**2. [Rule 3 - Blocking] Adapted integration tests for InMemoryBackend session lookup**
- **Found during:** Task 2 (Integration tests)
- **Issue:** SSE session endpoint checks coordinator.GetSessionAsync which queries InMemoryBackend, not the database. Seeding sessions only in DB caused 404 responses.
- **Fix:** Created SeedSessionEverywhere helper that seeds sessions in both InMemoryBackend (for coordinator lookup) and DB (for FK constraints). Added event emission triggers for SSE content-type tests.
- **Files modified:** tests/AgentHub.Tests/SseStreamingTests.cs
- **Verification:** All 5 integration tests pass
- **Committed in:** c942d14 (Task 2 commit)

---

**Total deviations:** 2 auto-fixed (1 bug, 1 blocking)
**Impact on plan:** Both auto-fixes necessary for correct compilation and test execution. No scope creep.

## Issues Encountered
- Concurrent Plan 01-03 execution modified Program.cs alongside this plan -- both plans' changes merged cleanly since they affected different sections (event service DI vs agent adapter DI)
- SSE integration tests required careful handling of long-running streams (ResponseHeadersRead + event emission triggers to avoid 499 client-disconnect responses)
- Maui project has pre-existing build errors (out of scope for Phase 1)

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- DurableEventService ready for use by SSH backend (Phase 2) to emit events during session execution
- Fleet-wide SSE endpoint ready for Phase 4 dashboard to subscribe to all events
- Event replay infrastructure supports client reconnection scenarios for the CLI and web dashboard
- All 48 tests pass (Plan 01: 10, Plan 02: 13, other: 25)

## Self-Check: PASSED

All 4 created files verified present. All 2 task commits verified in git log.

---
*Phase: 01-foundation-and-event-infrastructure*
*Completed: 2026-03-08*
