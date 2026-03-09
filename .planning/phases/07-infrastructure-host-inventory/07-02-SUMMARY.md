---
phase: 07-infrastructure-host-inventory
plan: 02
subsystem: infra, monitoring
tags: [ssh, background-service, inventory, host-discovery, version-detection]

# Dependency graph
requires:
  - phase: 07-infrastructure-host-inventory
    plan: 01
    provides: "HostMetricCache, HostEntity.InventoryJson, HostInventory/AgentInfo contracts, agents.json"
provides:
  - "HostInventoryPollingService for SSH-based agent CLI discovery with version detection"
  - "OS-specific composite SSH probe commands (Windows, Linux, macOS)"
  - "ParseInventoryOutput for JSON probe result parsing"
  - "ExtractVersion and ResolveCapabilities for agents.json-driven capability mapping"
  - "POST /api/hosts/{hostId}/refresh-inventory for on-demand single-host refresh"
  - "POST /api/hosts/refresh-inventory for on-demand full refresh"
  - "HostMetricCache integration in both polling services"
affects: [07-03, 08-multi-agent-dispatch, 09-interactive-sessions]

# Tech tracking
tech-stack:
  added: []
  patterns: [singleton + hosted service dual registration for injectable background services, printf JSON building for no-dependency SSH probes]

key-files:
  created:
    - src/AgentHub.Orchestration/Monitoring/HostInventoryPollingService.cs
    - tests/AgentHub.Tests/HostInventoryTests.cs
  modified:
    - src/AgentHub.Orchestration/Monitoring/HostMetricPollingService.cs
    - src/AgentHub.Service/Program.cs

key-decisions:
  - "Singleton + AddHostedService pattern for HostInventoryPollingService enables DI injection into API endpoints"
  - "printf/echo JSON construction in SSH probes avoids jq dependency on remote hosts"
  - "agents.json read from file on each poll cycle (hourly) rather than IConfiguration binding for hot config updates"

patterns-established:
  - "Dual registration: AddSingleton<T>() + AddHostedService(sp => sp.GetRequired<T>()) for injectable background services"
  - "Composite SSH probe: single command per host returning JSON, each agent probe independent with || true"

requirements-completed: [INVT-01, INVT-02, INVT-03, INVT-04]

# Metrics
duration: 4min
completed: 2026-03-09
---

# Phase 7 Plan 02: Inventory Polling Service Summary

**SSH-based HostInventoryPollingService with OS-specific composite probes, version extraction, capability resolution, and on-demand refresh API endpoints**

## Performance

- **Duration:** 4 min
- **Started:** 2026-03-09T20:57:24Z
- **Completed:** 2026-03-09T21:01:34Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments
- Built HostInventoryPollingService following exact HostMetricPollingService pattern with 60-min configurable poll interval
- OS-specific composite SSH probe commands for Windows (PowerShell), Linux (bash + df -BG), macOS (bash + df -g) -- no jq dependency
- ParseInventoryOutput handles valid, partial, and malformed JSON; ExtractVersion + ResolveCapabilities map raw CLI output to structured capability data
- HostMetricPollingService now updates HostMetricCache alongside DB writes for warm cache
- Two on-demand refresh endpoints: single-host and full refresh, both returning 202 Accepted
- 16 new inventory tests, 249 total tests passing with zero regressions

## Task Commits

Each task was committed atomically:

1. **Task 1: HostInventoryPollingService with composite SSH probing and output parsing**
   - `11c80bd` (feat) - HostInventoryPollingService with SSH probing, parsing, and version resolution
2. **Task 2: API endpoints, service registration, and HostMetricPollingService cache integration**
   - `d3cfe75` (feat) - API endpoints, service registration, and cache integration

## Files Created/Modified
- `src/AgentHub.Orchestration/Monitoring/HostInventoryPollingService.cs` - SSH-based inventory polling background service with AgentConfig record
- `tests/AgentHub.Tests/HostInventoryTests.cs` - 16 unit tests for probe generation, parsing, version extraction, and capability resolution
- `src/AgentHub.Orchestration/Monitoring/HostMetricPollingService.cs` - Added HostMetricCache injection and UpdateMetrics call
- `src/AgentHub.Service/Program.cs` - Registered HostMetricCache, HostInventoryPollingService, and refresh API endpoints

## Decisions Made
- Singleton + AddHostedService dual registration for HostInventoryPollingService -- enables injecting the service directly into API endpoints for on-demand refresh
- printf/echo JSON construction in bash probes -- avoids jq dependency on remote hosts per research recommendation
- agents.json read from file on each poll cycle -- cheap for hourly polling, allows hot config updates without restart

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- HostInventoryPollingService ready to discover agents on SSH hosts
- HostMetricCache receiving data from both polling services (metrics at 30s, inventory at 1hr)
- On-demand refresh endpoints ready for UI integration in Plan 03
- All contracts (HostInventory, AgentInfo) available for dashboard display

## Self-Check: PASSED

All 4 key files verified present. Both task commits verified in git log.

---
*Phase: 07-infrastructure-host-inventory*
*Completed: 2026-03-09*
