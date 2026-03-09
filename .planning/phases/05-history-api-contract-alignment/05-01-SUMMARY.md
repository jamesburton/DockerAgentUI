---
phase: 05-history-api-contract-alignment
plan: 01
subsystem: api
tags: [history, pagination, dto, envelope, kind-filter, integration-tests]

# Dependency graph
requires:
  - phase: 02-execution-engine
    provides: "SessionEventEntity, EntityMappers.ToDto(), DurableEventService"
  - phase: 03-cli-ux
    provides: "AgentHubApiClient, SessionLogsCommand"
provides:
  - "History endpoint with typed SessionEvent DTOs in {items, totalCount} envelope"
  - "Pagination (page/pageSize) and comma-separated kind filtering with 400 validation"
  - "CLI client envelope deserialization with metadata display"
affects: [06-web-history-alignment]

# Tech tracking
tech-stack:
  added: [JsonStringEnumConverter]
  patterns: [envelope-response-for-history, kind-filter-with-validation]

key-files:
  created: []
  modified:
    - src/AgentHub.Service/Program.cs
    - src/AgentHub.Cli/Api/AgentHubApiClient.cs
    - src/AgentHub.Cli/Commands/Session/SessionLogsCommand.cs
    - tests/AgentHub.Tests/SessionHistoryTests.cs

key-decisions:
  - "JsonStringEnumConverter on history endpoint for enum-as-string serialization (SessionEventKind)"
  - "Static HistoryJson.Options field for reusable JSON serializer config"

patterns-established:
  - "Kind filter validation: comma-split, case-insensitive Enum.TryParse, 400 on invalid"
  - "Tuple return from API client for envelope responses: (List<T> Items, int TotalCount)"

requirements-completed: [SESS-05, CLI-01]

# Metrics
duration: 8min
completed: 2026-03-09
---

# Phase 5 Plan 1: History API Contract Alignment Summary

**Rewritten history endpoint returning typed SessionEvent DTOs with pagination envelope, kind filtering, 400 validation, and CLI envelope deserialization with metadata display**

## Performance

- **Duration:** 8 min
- **Started:** 2026-03-09T12:13:54Z
- **Completed:** 2026-03-09T12:21:42Z
- **Tasks:** 2
- **Files modified:** 4

## Accomplishments
- History endpoint returns proper SessionEvent DTOs via EntityMappers.ToDto() instead of anonymous objects with raw MetaJson
- Response wrapped in {items, totalCount} envelope with page/pageSize pagination and 500 max page size
- Comma-separated, case-insensitive kind filter with 400 Bad Request for invalid values
- CLI client updated to deserialize envelope and render metadata on dimmed second line
- 8 new/updated integration tests covering envelope shape, pagination, filtering, meta deserialization

## Task Commits

Each task was committed atomically:

1. **Task 1: Rewrite history endpoint and add integration tests** - `1e3733f` (feat)
2. **Task 2: Update CLI client for envelope response** - `e91290d` (feat)

## Files Created/Modified
- `src/AgentHub.Service/Program.cs` - Rewritten history endpoint with ToDto(), pagination, kind filter, JsonStringEnumConverter
- `src/AgentHub.Cli/Api/AgentHubApiClient.cs` - Envelope-aware GetSessionHistoryAsync returning tuple, SessionHistoryResponse record
- `src/AgentHub.Cli/Commands/Session/SessionLogsCommand.cs` - Tuple destructuring in LoadHistoryAsync, dimmed metadata rendering
- `tests/AgentHub.Tests/SessionHistoryTests.cs` - 8 new/updated tests: envelope shape, pagination, kind filter, multi-kind, invalid kind 400, totalCount reflects filter, meta deserialized, default page size

## Decisions Made
- Used JsonStringEnumConverter on history endpoint only (via static HistoryJson.Options) to serialize SessionEventKind as string names rather than adding global JSON config
- Added static HistoryJson partial class at file scope for reusable options

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Added JsonStringEnumConverter for enum serialization**
- **Found during:** Task 1 (GREEN phase)
- **Issue:** Results.Json default serialization writes SessionEventKind as integer, but tests expect string enum name (e.g., "StdOut")
- **Fix:** Created HistoryJson.Options with JsonStringEnumConverter, passed to Results.Json call
- **Files modified:** src/AgentHub.Service/Program.cs
- **Verification:** GetSessionHistory_WithKindFilter test passes with string comparison
- **Committed in:** 1e3733f (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (1 bug)
**Impact on plan:** Essential for correct enum serialization. No scope creep.

## Issues Encountered
- File lock on .dswa.cache.json during first test run (transient Windows file lock) - resolved on retry

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- History API contract is aligned with typed DTOs, pagination, and filtering
- Web dashboard can now consume the same envelope format
- All 15 session history integration tests pass

---
*Phase: 05-history-api-contract-alignment*
*Completed: 2026-03-09*
