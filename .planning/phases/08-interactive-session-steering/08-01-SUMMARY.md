---
phase: 08-interactive-session-steering
plan: 01
subsystem: orchestration
tags: [steering, follow-up, host-daemon, event-sourcing, delivery-confirmation]

requires:
  - phase: 07-infrastructure-and-host-inventory
    provides: Host daemon protocol, SSH backend, session coordinator

provides:
  - IsFollowUp flag on SendInputRequest for follow-up instruction identification
  - SteeringInput and SteeringDelivered session event kinds
  - HostCommand.SendInput constant and SendInputPayload record
  - HostCommandProtocol.CreateSendInput factory method
  - Delivery confirmation via Task<bool> return from SendInputAsync
  - Coordinator steering event emission pipeline
  - API endpoint returning delivery status JSON

affects: [08-02, 08-03, session-history, event-streaming]

tech-stack:
  added: []
  patterns:
    - "Backward-compatible record extension via trailing optional parameters"
    - "Event emission before/after backend call for observability"
    - "Bool return from backend for delivery confirmation"

key-files:
  created:
    - tests/AgentHub.Tests/SteeringTests.cs
  modified:
    - src/AgentHub.Contracts/Models.cs
    - src/AgentHub.Orchestration/Abstractions.cs
    - src/AgentHub.Orchestration/HostDaemon/HostDaemonModels.cs
    - src/AgentHub.Orchestration/HostDaemon/HostCommandProtocol.cs
    - src/AgentHub.Orchestration/Backends/SshBackend.cs
    - src/AgentHub.Orchestration/Backends/InMemoryBackend.cs
    - src/AgentHub.Orchestration/Backends/NomadBackend.cs
    - src/AgentHub.Orchestration/Coordinator/SessionCoordinator.cs
    - src/AgentHub.Service/Program.cs

key-decisions:
  - "IsFollowUp added as last parameter with default false for backward compatibility"
  - "SteeringInput/SteeringDelivered appended to end of enum to preserve integer stability"
  - "SshBackend wraps send-input in try/catch returning false on failure (no retry per design)"

patterns-established:
  - "Steering events emitted only when IsFollowUp=true, preserving existing input behavior"
  - "API returns { delivered: bool } instead of bare 202 Accepted for delivery status"

requirements-completed: [INTER-01, INTER-03]

duration: 8min
completed: 2026-03-10
---

# Phase 8 Plan 1: Steering Contracts and Pipeline Summary

**Steering pipeline with IsFollowUp flag, SteeringInput/SteeringDelivered events, protocol-based SSH delivery, and bool delivery confirmation through coordinator to API**

## Performance

- **Duration:** 8 min
- **Started:** 2026-03-10T01:49:10Z
- **Completed:** 2026-03-10T01:57:05Z
- **Tasks:** 2
- **Files modified:** 12

## Accomplishments
- Extended SendInputRequest with IsFollowUp flag (backward compatible with all existing callers)
- Added SteeringInput and SteeringDelivered event kinds to SessionEventKind enum
- Built HostCommandProtocol.CreateSendInput factory method and SendInputPayload record
- Changed ISessionBackend.SendInputAsync to return Task<bool> for delivery confirmation
- Coordinator emits SteeringInput before backend call and SteeringDelivered/warning after
- SshBackend uses structured HostCommandProtocol for send-input with acknowledgment parsing
- API endpoint returns { delivered: true/false } JSON instead of bare 202
- 17 unit tests covering contracts and pipeline behavior

## Task Commits

Each task was committed atomically:

1. **Task 1: Extend contracts and protocol for steering** - `ffda04e` (feat)
2. **Task 2: Update backends, coordinator, and API endpoint for steering delivery** - `fe12d20` (feat)

_Both tasks followed TDD: RED (failing tests) then GREEN (implementation)_

## Files Created/Modified
- `src/AgentHub.Contracts/Models.cs` - Added IsFollowUp to SendInputRequest, SteeringInput/SteeringDelivered to SessionEventKind
- `src/AgentHub.Orchestration/Abstractions.cs` - Changed SendInputAsync return types to Task<bool>
- `src/AgentHub.Orchestration/HostDaemon/HostDaemonModels.cs` - Added SendInput constant and SendInputPayload record
- `src/AgentHub.Orchestration/HostDaemon/HostCommandProtocol.cs` - Added CreateSendInput factory method
- `src/AgentHub.Orchestration/Backends/SshBackend.cs` - Protocol-based send-input with acknowledgment return
- `src/AgentHub.Orchestration/Backends/InMemoryBackend.cs` - Returns true (always succeeds)
- `src/AgentHub.Orchestration/Backends/NomadBackend.cs` - Returns Task<bool> (stub)
- `src/AgentHub.Orchestration/Coordinator/SessionCoordinator.cs` - Steering event emission and delivery confirmation
- `src/AgentHub.Service/Program.cs` - API returns { delivered } JSON
- `tests/AgentHub.Tests/SteeringTests.cs` - 17 tests: 12 contract + 5 pipeline
- `tests/AgentHub.Tests/SessionCoordinatorTests.cs` - Updated mock backend for Task<bool>
- `tests/AgentHub.Tests/SessionCoordinatorApprovalTests.cs` - Updated mock backend for Task<bool>

## Decisions Made
- IsFollowUp added as last parameter with default false for backward compatibility
- SteeringInput/SteeringDelivered appended to end of enum to preserve integer stability
- SshBackend wraps send-input in try/catch returning false on failure (no retry per design)
- Steering events only emitted when IsFollowUp=true, preserving existing non-steering behavior

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Updated mock backends in existing test files**
- **Found during:** Task 1
- **Issue:** Existing test mocks (TrackingBackend, ApprovalTestBackend) implemented old Task return type
- **Fix:** Updated both to return Task<bool> (Task.FromResult(true))
- **Files modified:** tests/AgentHub.Tests/SessionCoordinatorTests.cs, tests/AgentHub.Tests/SessionCoordinatorApprovalTests.cs
- **Verification:** All existing tests continue to pass
- **Committed in:** ffda04e (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** Necessary interface contract update. No scope creep.

## Issues Encountered
- Process lock on DLLs (AgentHub.Service and AgentHub.Web running) -- killed processes to allow build
- Pre-existing flaky SSE streaming test (FleetEvent_StreamsEventsFromAllSessions) -- passes on re-run, unrelated to changes

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Steering contracts and pipeline complete, ready for Phase 8 Plan 2 (UI integration)
- SteeringInput/SteeringDelivered events queryable via existing session history API
- API endpoint returns delivery status for UI consumption

---
*Phase: 08-interactive-session-steering*
*Completed: 2026-03-10*
