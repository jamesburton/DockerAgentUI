---
phase: 07-infrastructure-host-inventory
plan: 01
subsystem: database, infra
tags: [ef-core, sqlite, migrations, concurrent-dictionary, inventory, multi-agent]

# Dependency graph
requires:
  - phase: 06-fleet-dashboard
    provides: "HostEntity, SessionEntity, EntityMappers, AgentHubDbContext base"
provides:
  - "InventoryJson column on HostEntity for agent inventory storage"
  - "ParentSessionId/DispatchId columns on SessionEntity for multi-agent coordination"
  - "HostInventory and AgentInfo contract records for API consumers"
  - "HostMetricCache singleton for thread-safe in-memory metric/inventory storage"
  - "agents.json config defining known agent CLIs"
affects: [07-02, 07-03, 08-multi-agent-dispatch, 09-interactive-sessions]

# Tech tracking
tech-stack:
  added: []
  patterns: [ConcurrentDictionary AddOrUpdate for partial updates, self-referencing FK with SetNull]

key-files:
  created:
    - src/AgentHub.Orchestration/Monitoring/HostMetricCache.cs
    - src/AgentHub.Orchestration/Migrations/20260309204051_AddInventoryColumns.cs
    - config/agents.json
    - tests/AgentHub.Tests/InventoryMigrationTests.cs
    - tests/AgentHub.Tests/HostMetricCacheTests.cs
  modified:
    - src/AgentHub.Orchestration/Data/Entities/HostEntity.cs
    - src/AgentHub.Orchestration/Data/Entities/SessionEntity.cs
    - src/AgentHub.Orchestration/Data/AgentHubDbContext.cs
    - src/AgentHub.Orchestration/Data/EntityMappers.cs
    - src/AgentHub.Contracts/Models.cs

key-decisions:
  - "Used ConcurrentDictionary.AddOrUpdate for atomic partial metric/inventory updates"
  - "Self-referencing FK on SessionEntity with SetNull delete behavior for parent session cleanup"

patterns-established:
  - "Partial cache update pattern: AddOrUpdate preserving non-updated fields"
  - "Self-referencing FK pattern: parent-child session relationships"

requirements-completed: [INFRA-01, INFRA-02]

# Metrics
duration: 6min
completed: 2026-03-09
---

# Phase 7 Plan 01: Data Foundation Summary

**EF Core migration adding ParentSessionId/DispatchId/InventoryJson columns, HostMetricCache singleton with partial-update support, and agents.json config**

## Performance

- **Duration:** 6 min
- **Started:** 2026-03-09T20:38:19Z
- **Completed:** 2026-03-09T20:44:16Z
- **Tasks:** 2
- **Files modified:** 12

## Accomplishments
- Added InventoryJson column to HostEntity and HostInventory/AgentInfo contract records for API consumers
- Added ParentSessionId (self-referencing FK with SetNull) and DispatchId to SessionEntity for multi-agent coordination
- Implemented HostMetricCache with thread-safe ConcurrentDictionary backing and partial-update methods
- Created agents.json config with claude and codex agent CLI definitions
- 12 new tests (5 migration/mapper + 7 cache), 233 total tests passing with zero regressions

## Task Commits

Each task was committed atomically:

1. **Task 1: Schema migration, entity extensions, contract types, and mappers**
   - `e39b102` (test) - failing tests for inventory schema and mapper extensions
   - `2596657` (feat) - inventory schema, contract types, and agent config
2. **Task 2: HostMetricCache singleton with thread-safe operations**
   - `d86b4af` (test) - failing tests for HostMetricCache
   - `9f172d6` (feat) - implement HostMetricCache with thread-safe operations

_Note: TDD tasks have RED (test) and GREEN (feat) commits._

## Files Created/Modified
- `src/AgentHub.Orchestration/Data/Entities/HostEntity.cs` - Added InventoryJson column
- `src/AgentHub.Orchestration/Data/Entities/SessionEntity.cs` - Added ParentSessionId, DispatchId columns
- `src/AgentHub.Orchestration/Data/AgentHubDbContext.cs` - Self-referencing FK configuration for ParentSessionId
- `src/AgentHub.Orchestration/Data/EntityMappers.cs` - InventoryJson deserialization in ToDto/ToEntity
- `src/AgentHub.Contracts/Models.cs` - HostInventory, AgentInfo records; extended HostRecord with Inventory
- `src/AgentHub.Orchestration/Monitoring/HostMetricCache.cs` - Thread-safe in-memory cache with partial updates
- `src/AgentHub.Orchestration/Migrations/20260309204051_AddInventoryColumns.cs` - EF Core migration
- `config/agents.json` - Agent CLI definitions for SSH probing
- `tests/AgentHub.Tests/InventoryMigrationTests.cs` - 5 tests for schema and mapper round-trips
- `tests/AgentHub.Tests/HostMetricCacheTests.cs` - 7 tests for cache operations and concurrency

## Decisions Made
- Used ConcurrentDictionary.AddOrUpdate for atomic partial metric/inventory updates -- ensures no data loss on concurrent partial writes
- Self-referencing FK on SessionEntity with SetNull delete behavior -- allows parent session deletion without cascading to children

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
None

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Schema foundation ready for Plan 02 (SSH inventory polling service)
- HostMetricCache ready to receive data from polling service
- agents.json defines probe targets for inventory collection
- HostRecord.Inventory available for API consumers in Plan 03

## Self-Check: PASSED

All 6 key files verified present. All 4 task commits verified in git log.

---
*Phase: 07-infrastructure-host-inventory*
*Completed: 2026-03-09*
