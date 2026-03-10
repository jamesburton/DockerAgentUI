---
phase: 09-git-worktree-isolation
plan: 01
subsystem: orchestration
tags: [git, worktree, ssh, branch-naming, diff-parsing]

# Dependency graph
requires:
  - phase: 07-host-inventory
    provides: SSH backend infrastructure, ISshHostConnection
provides:
  - WorktreeService with SSH-based git worktree operations
  - BranchNameGenerator for session branch naming
  - DiffStatsParser for structured diff output
  - DiffStats/FileDiffStat contract records
  - SshBackend worktree lifecycle integration (create before agent, cleanup after stop)
  - KeepBranch end-to-end wiring
affects: [09-02, ui-worktree-surfaces]

# Tech tracking
tech-stack:
  added: []
  patterns: [static-utility-classes-for-pure-logic, ssh-command-construction-with-shell-escaping]

key-files:
  created:
    - src/AgentHub.Orchestration/Worktree/WorktreeService.cs
    - src/AgentHub.Orchestration/Worktree/BranchNameGenerator.cs
    - src/AgentHub.Orchestration/Worktree/DiffStatsParser.cs
    - tests/AgentHub.Tests/BranchNameGeneratorTests.cs
    - tests/AgentHub.Tests/DiffStatsParserTests.cs
    - tests/AgentHub.Tests/WorktreeServiceTests.cs
    - tests/AgentHub.Tests/SshBackendWorktreeTests.cs
  modified:
    - src/AgentHub.Contracts/Models.cs
    - src/AgentHub.Orchestration/Data/Entities/SessionEntity.cs
    - src/AgentHub.Orchestration/Data/EntityMappers.cs
    - src/AgentHub.Orchestration/Backends/SshBackend.cs
    - src/AgentHub.Service/Program.cs
    - tests/AgentHub.Tests/SshBackendTests.cs

key-decisions:
  - "Static utility classes for BranchNameGenerator and DiffStatsParser (pure logic, no DI needed)"
  - "Shell escaping via single-quote wrapping with internal quote escaping for SSH commands"
  - "Force-kill keeps worktree by default; only cleans if CleanupPolicy is explicitly 'cleanup'"
  - "Generated regex for BranchNameGenerator slug sanitization (compile-time performance)"

patterns-established:
  - "Worktree directory layout: {repoRoot}/.worktrees/{sessionId}"
  - "Branch naming convention: agenthub/{shortId}-{slugifiedPrompt}"

requirements-completed: [WKTREE-01, WKTREE-02, WKTREE-03]

# Metrics
duration: 8min
completed: 2026-03-10
---

# Phase 9 Plan 01: Core Worktree Engine Summary

**Git worktree lifecycle engine with SSH-based create/cleanup, slugified branch naming, and diff parsing integrated into SshBackend session lifecycle**

## Performance

- **Duration:** 8 min
- **Started:** 2026-03-10T09:30:37Z
- **Completed:** 2026-03-10T09:38:47Z
- **Tasks:** 2 (TDD + integration)
- **Files modified:** 13

## Accomplishments
- WorktreeService with SSH-based git worktree create, cleanup (stash+remove+branch-delete), diff stats, and orphan detection
- BranchNameGenerator producing agenthub/{8-char-id}-{slugified-prompt} with truncation, sanitization, and timestamp fallback
- DiffStatsParser handling numstat format including binary files with status detection
- SshBackend creates worktree before agent start when WorktreeId is set, cleans up after stop
- KeepBranch parameter wired end-to-end from StartSessionRequest through SessionEntity to CleanupWorktreeAsync

## Task Commits

Each task was committed atomically:

1. **Task 1 (RED): Failing tests** - `576c9b7` (test)
2. **Task 1 (GREEN): Implementation** - `6634018` (feat)
3. **Task 2: SshBackend integration** - `1127239` (feat)

_Task 1 followed TDD: RED (failing tests) then GREEN (implementation passing all tests)_

## Files Created/Modified
- `src/AgentHub.Orchestration/Worktree/WorktreeService.cs` - SSH-based git worktree operations (create, cleanup, diff, orphan)
- `src/AgentHub.Orchestration/Worktree/BranchNameGenerator.cs` - Branch name generation with slugified prompt
- `src/AgentHub.Orchestration/Worktree/DiffStatsParser.cs` - Parse git diff --numstat into DiffStats records
- `src/AgentHub.Contracts/Models.cs` - DiffStats, FileDiffStat records; KeepBranch on StartSessionRequest; WorktreeBranch on SessionSummary
- `src/AgentHub.Orchestration/Data/Entities/SessionEntity.cs` - WorktreeBranch and KeepBranch columns
- `src/AgentHub.Orchestration/Data/EntityMappers.cs` - WorktreeBranch mapping in both directions
- `src/AgentHub.Orchestration/Backends/SshBackend.cs` - Worktree lifecycle integration with CleanupWorktreeIfNeeded
- `src/AgentHub.Service/Program.cs` - WorktreeService DI registration
- `tests/AgentHub.Tests/BranchNameGeneratorTests.cs` - 7 unit tests for branch naming
- `tests/AgentHub.Tests/DiffStatsParserTests.cs` - 7 unit tests for diff parsing
- `tests/AgentHub.Tests/WorktreeServiceTests.cs` - 6 unit tests for SSH command construction
- `tests/AgentHub.Tests/SshBackendWorktreeTests.cs` - 5 integration tests for lifecycle
- `tests/AgentHub.Tests/SshBackendTests.cs` - Updated constructor for WorktreeService dependency

## Decisions Made
- Static utility classes for BranchNameGenerator and DiffStatsParser since they contain pure logic with no dependencies
- Shell escaping via single-quote wrapping (`'value'`) with internal single-quote escaping (`'\''`)
- Force-kill keeps worktree by default (auto policy on force-kill = skip cleanup); only cleans if CleanupPolicy explicitly set to "cleanup"
- Used GeneratedRegex for compile-time regex in BranchNameGenerator

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed mock response ordering in SshBackendWorktreeTests**
- **Found during:** Task 2
- **Issue:** Mock connection responses were enqueued in wrong order for worktree-enabled StartAsync flow; git rev-parse consumed the start-session JSON response, causing JSON parse error
- **Fix:** Reordered mock responses to match actual execution flow: rev-parse response first, worktree add second, start-session protocol response third
- **Files modified:** tests/AgentHub.Tests/SshBackendWorktreeTests.cs
- **Verification:** All 37 tests pass
- **Committed in:** 1127239

---

**Total deviations:** 1 auto-fixed (1 bug)
**Impact on plan:** Test-only fix, no scope creep.

## Issues Encountered
None beyond the mock response ordering issue documented above.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- WorktreeService, BranchNameGenerator, DiffStatsParser available for Plan 02 to build user-facing surfaces
- DiffStats/FileDiffStat records in Contracts ready for API endpoints
- SessionEntity.WorktreeBranch persisted for diff lookups
- KeepBranch wired end-to-end, ready for CLI --keep-branch flag in Plan 02

## Self-Check: PASSED

All 8 created files verified present. All 3 task commits (576c9b7, 6634018, 1127239) verified in git log. 37/37 tests passing.

---
*Phase: 09-git-worktree-isolation*
*Completed: 2026-03-10*
