---
phase: 09-git-worktree-isolation
plan: 02
subsystem: ui, api, cli
tags: [worktree, diff-stats, blazor, spectre-console, api-endpoints, repo-path]

# Dependency graph
requires:
  - phase: 09-01
    provides: WorktreeService, BranchNameGenerator, DiffStatsParser, SshBackend worktree lifecycle
provides:
  - API endpoints for diff stats and worktree cleanup
  - CLI commands for session diff and worktree cleanup
  - CLI --worktree and --repo-path flags on session start
  - CLI host set-repo command for default repo path
  - Web UI diff stats panel on session detail
  - Web UI worktree and keep-branch toggles in launch dialog
  - Web UI repo path field in launch dialog
  - RepoPath infrastructure (host default + session-level + fallback chain)
  - PATCH /api/hosts/{id} for host config updates
affects: [future-repo-management]

# Tech tracking
tech-stack:
  added: []
  patterns: [repo-path-fallback-chain, direct-db-lookup-for-endpoints]

key-files:
  created:
    - src/AgentHub.Cli/Commands/Session/SessionDiffCommand.cs
    - src/AgentHub.Cli/Commands/Worktree/WorktreeCleanupCommand.cs
    - src/AgentHub.Orchestration/Migrations/20260310143000_AddRepoPathColumns.cs
  modified:
    - src/AgentHub.Service/Program.cs
    - src/AgentHub.Web/Components/Pages/SessionDetail.razor
    - src/AgentHub.Web/Components/Shared/LaunchDialog.razor
    - src/AgentHub.Cli/Program.cs
    - src/AgentHub.Cli/Api/AgentHubApiClient.cs
    - src/AgentHub.Contracts/Models.cs
    - src/AgentHub.Orchestration/Backends/SshBackend.cs
    - src/AgentHub.Orchestration/Data/Entities/SessionEntity.cs
    - src/AgentHub.Orchestration/Data/Entities/HostEntity.cs
    - src/AgentHub.Orchestration/Data/EntityMappers.cs
    - src/AgentHub.Orchestration/Data/HostSeedingService.cs
    - config/hosts.json

key-decisions:
  - "Repo path fallback chain: request.RepoPath > host.DefaultRepoPath > git rev-parse"
  - "Diff endpoint queries DB directly instead of coordinator.GetSessionAsync to avoid backend iteration issues"
  - "PATCH endpoint for host config updates (DefaultRepoPath)"
  - "WorktreeId belongs on StartSessionRequest (top-level), not SessionRequirements"

patterns-established:
  - "Repo path resolution: explicit > host default > auto-detect"
  - "Host config updates via PATCH /api/hosts/{id}"

requirements-completed: [WKTREE-04]

# Metrics
duration: 15min
completed: 2026-03-10
---

# Phase 9 Plan 02: Worktree UI Surfaces Summary

**API endpoints, CLI commands, and Web UI panels for worktree diff stats, cleanup, and launch with repo path configuration**

## Performance

- **Duration:** ~15 min (across executor + manual bug fixes)
- **Started:** 2026-03-10T14:00:00Z
- **Completed:** 2026-03-10T14:30:00Z
- **Tasks:** 3 (API+CLI, Web UI, verification+fixes)
- **Files modified:** 15

## Accomplishments
- API endpoints for session diff stats and host worktree cleanup
- CLI commands: `ah session diff`, `ah worktree cleanup`, `ah host set-repo`
- CLI flags: `--worktree`, `--repo-path`, `--keep-branch` on session start
- Web UI: diff stats panel on session detail, worktree/keep-branch/repo-path in launch dialog
- Fixed 3 bugs: WorktreeId placement, session lookup, repo root detection
- Added RepoPath to session and host entities with EF migration

## Task Commits

1. **Task 1: API endpoints + CLI commands** - `0514809`, `9b18716`, `5cb17c6`, `130cb03`, `e2ea133`, `b1167a4` (feat/fix)
2. **Task 2: Web UI panels** - `9b18716` (feat)
3. **Task 3: Bug fixes + repo path infrastructure** - `0cea95b` (fix)

## Files Created/Modified
- `src/AgentHub.Service/Program.cs` - Diff/cleanup endpoints, PATCH host endpoint
- `src/AgentHub.Cli/Commands/Session/SessionDiffCommand.cs` - `ah session diff` command
- `src/AgentHub.Cli/Commands/Worktree/WorktreeCleanupCommand.cs` - `ah worktree cleanup` command
- `src/AgentHub.Cli/Program.cs` - --worktree, --repo-path flags, host set-repo command
- `src/AgentHub.Cli/Api/AgentHubApiClient.cs` - PatchHostAsync method
- `src/AgentHub.Web/Components/Pages/SessionDetail.razor` - Diff stats expansion panel
- `src/AgentHub.Web/Components/Shared/LaunchDialog.razor` - Worktree toggle, repo path field
- `src/AgentHub.Contracts/Models.cs` - RepoPath on StartSessionRequest, DefaultRepoPath on HostRecord
- `src/AgentHub.Orchestration/Backends/SshBackend.cs` - Repo path fallback chain, stored RepoPath
- `src/AgentHub.Orchestration/Data/Entities/SessionEntity.cs` - RepoPath column
- `src/AgentHub.Orchestration/Data/Entities/HostEntity.cs` - DefaultRepoPath column
- `src/AgentHub.Orchestration/Data/EntityMappers.cs` - DefaultRepoPath mapping
- `src/AgentHub.Orchestration/Data/HostSeedingService.cs` - DefaultRepoPath upsert
- `config/hosts.json` - defaultRepoPath field

## Decisions Made
- WorktreeId belongs on StartSessionRequest top-level, not inside SessionRequirements
- Diff endpoint queries DB directly to avoid backend iteration reliability issues
- Repo path uses 3-level fallback: explicit request > host default > git rev-parse auto-detect
- PATCH /api/hosts/{id} for host config updates (extensible for future fields)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] WorktreeId on wrong model**
- **Found during:** Task 3 (verification)
- **Issue:** LaunchDialog set WorktreeId on SessionRequirements but SshBackend reads request.WorktreeId (top-level)
- **Fix:** Moved WorktreeId to StartSessionRequest constructor in LaunchDialog
- **Committed in:** 0cea95b

**2. [Rule 1 - Bug] Session diff lookup fails**
- **Found during:** Task 3 (verification)
- **Issue:** Diff endpoint used coordinator.GetSessionAsync which iterates backends
- **Fix:** Direct DB query via db.Sessions.FirstOrDefaultAsync
- **Committed in:** 0cea95b

**3. [Rule 2 - Missing Critical] No repo path support**
- **Found during:** Task 3 (verification)
- **Issue:** All worktree ops assumed git rev-parse would find repo from SSH default dir
- **Fix:** Added RepoPath to session/host entities, fallback chain, UI/CLI fields
- **Committed in:** 0cea95b

---

**Total deviations:** 3 auto-fixed (2 bugs, 1 missing critical)
**Impact on plan:** Bug fixes essential for functionality. Repo path infrastructure prevents "cannot determine repo root" in all environments.

## Issues Encountered
- Running service/web processes locked DLLs during build — EF migration created manually

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Full worktree feature complete for Phase 9
- Repo management (importing remote repos, listing repos per host) identified as future work
- Ready for phase verification

## Self-Check: PASSED

All key files verified present. Commits verified in git log. 293/293 tests passing.

---
*Phase: 09-git-worktree-isolation*
*Completed: 2026-03-10*
