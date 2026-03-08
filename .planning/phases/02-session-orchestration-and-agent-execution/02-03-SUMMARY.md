---
phase: 02-session-orchestration-and-agent-execution
plan: 03
subsystem: security
tags: [approval-flow, sanitization, trust-tiers, sse, race-conditions, shell-injection]

requires:
  - phase: 02-session-orchestration-and-agent-execution
    provides: "ApprovalEntity, TrustTier enum, ScopedPolicyConfig, SessionEventKind.ApprovalRequest, IDbContextFactory pattern"
provides:
  - "ApprovalService with SSE-delivered approval requests and configurable timeout"
  - "ApprovalDecision enum and ApprovalContext record for approval flow"
  - "Extended BasicSanitizationService with shell injection, path traversal, env exfiltration, base64 detection"
  - "TrustTierDecision record with EvaluateWithTrustTier method"
  - "Configurable sanitization rules via ScopedPolicyConfig.DisallowedTools"
affects: [02-04]

tech-stack:
  added: []
  patterns: [approval-state-machine, trust-tier-gating, extended-sanitization, tcs-race-safety]

key-files:
  created:
    - src/AgentHub.Orchestration/Coordinator/ApprovalService.cs
    - tests/AgentHub.Tests/ApprovalServiceTests.cs
    - tests/AgentHub.Tests/SanitizationTests.cs
  modified:
    - src/AgentHub.Orchestration/Security/BasicSanitizationService.cs
    - src/AgentHub.Orchestration/AgentHub.Orchestration.csproj
    - src/AgentHub.Orchestration/Backends/SshBackend.cs

key-decisions:
  - "ApprovalService registered as singleton with ConcurrentDictionary for pending approvals and IDbContextFactory for scoped DB access"
  - "TrySetResult (not SetResult) for race-condition safety between timeout and manual resolution"
  - "Fire-and-forget DB updates on ResolveApproval to avoid blocking the caller"
  - "Dual regex patterns for env var exfiltration: tool-before-var and var-before-tool ordering"
  - "Safe default: unknown actions default to Prompt trust tier (require approval)"

patterns-established:
  - "ApprovalService.RequestApprovalAsync: Blocks session with TCS until resolved/timed-out/auto-approved"
  - "ApprovalService.ResolveApproval: TrySetResult for first-one-wins race handling"
  - "BasicSanitizationService.EvaluateWithTrustTier: Action classification per policy config"
  - "Overloaded Evaluate with optional ScopedPolicyConfig for configurable patterns"

requirements-completed: [AGENT-03, AGENT-04]

duration: 6min
completed: 2026-03-08
---

# Phase 2 Plan 3: Approval Gating and Extended Sanitization Summary

**Approval state machine with SSE delivery, configurable timeout actions, race-safe resolution, and extended sanitization detecting shell injection, path traversal, env var exfiltration, and base64 payloads**

## Performance

- **Duration:** 6 min
- **Started:** 2026-03-08T15:15:58Z
- **Completed:** 2026-03-08T15:22:39Z
- **Tasks:** 2
- **Files modified:** 6

## Accomplishments
- Built ApprovalService with full approval state machine: request, block, resolve/timeout/auto-approve
- Approval requests emitted as SSE events with approvalId in Meta for client resolution
- Configurable timeout behavior: auto-approve, stop, continue (default)
- Race-condition-safe resolution via TrySetResult (first-one-wins)
- SkipPermissionPrompts bypasses approval entirely (no event, no DB record)
- Extended BasicSanitizationService with 4 new pattern categories
- Trust tier evaluation: AlwaysAllow/Prompt/AlwaysDeny with safe Prompt default
- Configurable patterns via ScopedPolicyConfig.DisallowedTools
- All 53 new tests pass (11 approval + 42 sanitization), 131 total unit tests green

## Task Commits

Each task was committed atomically:

1. **Task 1: ApprovalService with state machine, SSE delivery, and timeout** - `e9d9139` (feat)
2. **Task 2: Extended sanitization with trust tiers and configurable rules** - `0cc308b` (feat)

_TDD approach: tests written first, then implementation to pass them._

## Files Created/Modified
- `src/AgentHub.Orchestration/Coordinator/ApprovalService.cs` - Approval state machine with SSE delivery, TCS-based blocking, configurable timeout
- `src/AgentHub.Orchestration/Security/BasicSanitizationService.cs` - Extended with shell injection, path traversal, env exfiltration, base64, trust tiers, configurable patterns
- `src/AgentHub.Orchestration/AgentHub.Orchestration.csproj` - Added Microsoft.Extensions.Configuration.Binder package
- `tests/AgentHub.Tests/ApprovalServiceTests.cs` - 11 tests: emit, persist, block, approve, deny, timeout actions, skip, race, context
- `tests/AgentHub.Tests/SanitizationTests.cs` - 42 tests: injection, traversal, exfiltration, base64, tiers, config, backward compat, edge cases

## Decisions Made
- ApprovalService as singleton with ConcurrentDictionary + IDbContextFactory (follows DurableEventService pattern from Phase 1)
- TrySetResult for race safety: timeout and manual resolve can fire simultaneously, first one wins
- Fire-and-forget DB updates on ResolveApproval to avoid blocking the resolution path
- Dual regex ordering for env var exfiltration (tool-before-var and var-before-tool)
- Unknown actions default to Prompt tier (safe default per research recommendation)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Fixed SshBackend.StopAsync interface mismatch**
- **Found during:** Task 1 (build failure)
- **Issue:** SshBackend.StopAsync(string, CancellationToken) did not match ISessionBackend.StopAsync(string, bool, CancellationToken) interface
- **Fix:** Added missing `bool forceKill` parameter to SshBackend.StopAsync
- **Files modified:** src/AgentHub.Orchestration/Backends/SshBackend.cs
- **Verification:** Build succeeded
- **Committed in:** e9d9139 (Task 1 commit)

**2. [Rule 3 - Blocking] Added Microsoft.Extensions.Configuration.Binder package**
- **Found during:** Task 1 (build failure)
- **Issue:** SshBackend uses configuration.GetValue<int?>() which requires Configuration.Binder package not referenced in csproj
- **Fix:** Added PackageReference for Microsoft.Extensions.Configuration.Binder 10.0.3
- **Files modified:** src/AgentHub.Orchestration/AgentHub.Orchestration.csproj
- **Verification:** Build succeeded, dotnet restore resolved package
- **Committed in:** e9d9139 (Task 1 commit)

---

**Total deviations:** 2 auto-fixed (2 blocking)
**Impact on plan:** Both fixes addressed pre-existing build issues from Plan 02-02's SshBackend rewrite. No scope creep.

## Issues Encountered
- 8 API/SSE integration tests (ApiEndpointTests, SseStreamingTests) fail due to DI wiring issues from SshBackend constructor changes in Plan 02-02. These are pre-existing failures not caused by Plan 02-03 changes. All 131 unit tests pass.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- ApprovalService ready for SessionCoordinator integration (Plan 02-04 can call RequestApprovalAsync during destructive action handling)
- Extended sanitization ready for API boundary validation with trust tier checks
- TrustTierDecision integrates with ScopedPolicyConfig from Plan 02-01
- All foundation for the safety layer complete

---
*Phase: 02-session-orchestration-and-agent-execution*
*Completed: 2026-03-08*
