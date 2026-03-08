---
phase: 01-foundation-and-event-infrastructure
plan: 01
subsystem: database
tags: [ef-core, sqlite, persistence, entities, host-seeding, xunit]

requires:
  - phase: none
    provides: greenfield foundation

provides:
  - EF Core DbContext with Sessions, Events, Hosts DbSets
  - SessionEntity, SessionEventEntity, HostEntity persistence entities
  - EntityMappers for DTO-to-entity round-trip conversion
  - HostSeedingService to upsert hosts.json into DB on startup
  - DbHostRegistry for DB-backed host queries
  - Test project with xUnit, EF Core InMemory, and MVC Testing
  - AgentTypes.cs with AgentType enum and permission records

affects: [01-02, 01-03, 02-ssh-backend, event-streaming]

tech-stack:
  added: [Microsoft.EntityFrameworkCore.Sqlite 10.0.2, Microsoft.EntityFrameworkCore.Design 10.0.2, Microsoft.EntityFrameworkCore.InMemory 10.0.2, Microsoft.Extensions.Hosting.Abstractions, xunit 2.9.3, Microsoft.AspNetCore.Mvc.Testing 10.0.2]
  patterns: [separate EF entities from API DTOs, DbContextPool + DbContextFactory dual registration, HostedService for startup seeding, WAL mode for SQLite]

key-files:
  created:
    - src/AgentHub.Contracts/AgentTypes.cs
    - src/AgentHub.Orchestration/Data/AgentHubDbContext.cs
    - src/AgentHub.Orchestration/Data/Entities/SessionEntity.cs
    - src/AgentHub.Orchestration/Data/Entities/SessionEventEntity.cs
    - src/AgentHub.Orchestration/Data/Entities/HostEntity.cs
    - src/AgentHub.Orchestration/Data/EntityMappers.cs
    - src/AgentHub.Orchestration/Data/HostSeedingService.cs
    - src/AgentHub.Orchestration/Config/DbHostRegistry.cs
    - tests/AgentHub.Tests/AgentHub.Tests.csproj
    - tests/AgentHub.Tests/PersistenceTests.cs
    - tests/AgentHub.Tests/ApiEndpointTests.cs
  modified:
    - src/AgentHub.Orchestration/AgentHub.Orchestration.csproj
    - src/AgentHub.Service/AgentHub.Service.csproj
    - src/AgentHub.Service/Program.cs
    - AgentSafeEnv.sln

key-decisions:
  - "Dual DbContext registration: AddDbContextPool for scoped DI + AddDbContextFactory for singletons (HostSeedingService, DbHostRegistry)"
  - "Keep JsonHostRegistry intact as fallback; add DbHostRegistry as new DB-backed implementation"
  - "Store SessionState and SessionEventKind enums as strings in DB for readability"
  - "EF Core Design package added to both Orchestration and Service projects (required for migration tooling)"

patterns-established:
  - "Entity/DTO separation: EF entities in Data/Entities/, API DTOs in Contracts/Models.cs, mappers in EntityMappers.cs"
  - "Host seeding: IHostedService reads hosts.json on startup, upserts to DB (file wins for config, DB wins for runtime state)"
  - "SQLite PRAGMAs: WAL mode + busy_timeout=5000 applied after migration"
  - "Test project uses file-based SQLite for integration tests, InMemory provider for unit tests"

requirements-completed: [INFRA-03, INFRA-01]

duration: 7min
completed: 2026-03-08
---

# Phase 1 Plan 01: Persistence Foundation Summary

**EF Core SQLite persistence layer with Session/Event/Host entities, host seeding from hosts.json, DB-backed API endpoints, and xUnit test project with 10 passing tests**

## Performance

- **Duration:** 7 min
- **Started:** 2026-03-08T10:36:48Z
- **Completed:** 2026-03-08T10:44:04Z
- **Tasks:** 2
- **Files modified:** 15

## Accomplishments
- EF Core DbContext with three entity types (Session, SessionEvent, Host) and proper indexes for query performance
- Host seeding service that upserts hosts.json into the database on startup with merge strategy (config from file, runtime state from DB)
- API endpoints (/healthz, /api/hosts, /api/sessions) now served from SQLite-backed persistent store
- Test project with 7 persistence unit tests and 3 API integration tests, all passing

## Task Commits

Each task was committed atomically:

1. **Task 1: Create test project, EF Core entities, DbContext, and migrations** - `66bd527` (feat)
2. **Task 2 RED: Add persistence and API endpoint tests** - `477efd3` (test)
3. **Task 2 GREEN: Wire persistence, host seeding, DB-backed host registry** - `fc42ff3` (feat)

## Files Created/Modified
- `src/AgentHub.Contracts/AgentTypes.cs` - AgentType enum, AgentPermissions, AgentDefinition records
- `src/AgentHub.Orchestration/Data/AgentHubDbContext.cs` - EF Core DbContext with Sessions, Events, Hosts DbSets
- `src/AgentHub.Orchestration/Data/Entities/SessionEntity.cs` - Session persistence entity with navigation to events
- `src/AgentHub.Orchestration/Data/Entities/SessionEventEntity.cs` - Event entity with auto-increment ID for SSE replay
- `src/AgentHub.Orchestration/Data/Entities/HostEntity.cs` - Host persistence entity with runtime state fields
- `src/AgentHub.Orchestration/Data/EntityMappers.cs` - Static extension methods for DTO/entity round-trip mapping
- `src/AgentHub.Orchestration/Data/HostSeedingService.cs` - IHostedService that upserts hosts.json into DB on startup
- `src/AgentHub.Orchestration/Config/DbHostRegistry.cs` - IHostRegistry backed by EF Core queries
- `src/AgentHub.Service/Program.cs` - DbContext registration, migration, SQLite PRAGMAs, host seeding wiring
- `tests/AgentHub.Tests/PersistenceTests.cs` - 7 unit tests for CRUD, mappers, persistence across contexts
- `tests/AgentHub.Tests/ApiEndpointTests.cs` - 3 integration tests for healthz, hosts, sessions endpoints

## Decisions Made
- Used dual DbContext registration (AddDbContextPool + AddDbContextFactory) to support both scoped DI and singleton services
- Kept existing JsonHostRegistry intact as potential fallback; added DbHostRegistry as new primary implementation
- Stored enum values as strings in SQLite for human readability in DB inspection
- Added EF Core Design package to Service project (required by migration tooling for startup project)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added EF Core packages to Service project**
- **Found during:** Task 1 (Migration creation)
- **Issue:** `dotnet ef migrations` requires Microsoft.EntityFrameworkCore.Design in the startup project, not just the data project
- **Fix:** Added Microsoft.EntityFrameworkCore.Design and Microsoft.EntityFrameworkCore.Sqlite to AgentHub.Service.csproj
- **Files modified:** src/AgentHub.Service/AgentHub.Service.csproj
- **Verification:** Migration created successfully
- **Committed in:** 66bd527 (Task 1 commit)

**2. [Rule 3 - Blocking] Added Microsoft.Extensions.Hosting.Abstractions to Orchestration**
- **Found during:** Task 2 (HostSeedingService compilation)
- **Issue:** IHostedService and ILogger not available in class library project without hosting abstractions package
- **Fix:** Added Microsoft.Extensions.Hosting.Abstractions NuGet package to Orchestration project
- **Files modified:** src/AgentHub.Orchestration/AgentHub.Orchestration.csproj
- **Verification:** Build succeeded
- **Committed in:** fc42ff3 (Task 2 commit)

---

**Total deviations:** 2 auto-fixed (2 blocking dependency issues)
**Impact on plan:** Both auto-fixes necessary for compilation. No scope creep.

## Issues Encountered
- Maui project has pre-existing build errors (XAML parsing, missing API methods) -- excluded from solution build verification since it is out of scope for Phase 1
- xUnit v2 `using Xunit;` directive needed explicitly (not auto-imported by ImplicitUsings)

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- DbContext and entities ready for event streaming (Plan 01-02) to build DurableEventService on top
- Host seeding pattern established for other config seeding needs
- Test project infrastructure ready for additional test files
- Existing in-memory backends (InMemoryBackend, SshBackend, SessionCoordinator) still wired -- Plan 01-02/01-03 will evolve these to use EF Core persistence

## Self-Check: PASSED

All 11 created files verified present. All 3 task commits verified in git log.

---
*Phase: 01-foundation-and-event-infrastructure*
*Completed: 2026-03-08*
