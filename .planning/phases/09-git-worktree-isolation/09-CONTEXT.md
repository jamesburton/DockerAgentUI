# Phase 9: Git Worktree Isolation - Context

**Gathered:** 2026-03-10
**Status:** Ready for planning

<domain>
## Phase Boundary

Each agent session can operate in its own git worktree so parallel agents on the same repo cannot conflict. Users opt in with a `--worktree` flag. System creates the worktree on the remote host before agent launch, cleans up on session end, and provides diff stats for merge-readiness assessment.

</domain>

<decisions>
## Implementation Decisions

### Worktree creation flow
- Explicit `--worktree` flag opts in per session — default is no worktree (agent runs in main checkout)
- Worktree location configurable per host in host config, defaulting to a `.worktrees/` directory inside the repo root (gitignored)
- Worktree based on current HEAD of the default branch (main/master) — agent always starts from latest stable code
- Git worktree created on remote host via SSH before agent process starts — part of session launch pipeline in SshBackend

### Branch naming & identification
- Branch format: `agenthub/{sessionId}-{slug}` — namespaced prefix, session ID for uniqueness, slugified prompt summary for readability
- When no prompt is provided (file reference or fire-and-forget), fall back to timestamp suffix: `agenthub/{sessionId}-{YYYYMMDD-HHmm}`

### Cleanup & failure handling
- Cleanup respects the existing per-session CleanupPolicy flag — `--keep` or `--cleanup` override the default
- Default: clean up worktree on success, keep on failure/kill (carried from Phase 2)
- Branch cleanup is configurable: default delete both worktree and branch, `--keep-branch` flag preserves branch for cherry-picking
- Before cleanup, system runs `git stash` in the worktree to preserve any uncommitted work — stash accessible from main repo
- Manual cleanup command (`ah worktree cleanup`) for orphaned worktrees from crashed sessions — API endpoint + CLI command, operator-triggered

### Diff stats & merge-readiness
- Dedicated `ah session diff {id}` CLI command AND diff summary shown in session detail views (CLI watch + web detail page)
- CLI: default git diffstat format, `--detailed` flag for full Spectre.Console table (file path, status, lines changed)
- Web UI: presentation at Claude's discretion (collapsible panel or tab — consistent with existing MudBlazor patterns)

### Claude's Discretion
- Git version validation approach (pre-check vs try-and-handle)
- Prompt slugification algorithm (word count, char limit, sanitization rules)
- Fallback naming when no prompt available
- Diff base strategy (worktree creation point vs current HEAD)
- Web UI diff panel design (collapsible vs tab)
- SSH command construction for worktree operations (create, remove, prune)
- Error handling for worktree creation failures

</decisions>

<specifics>
## Specific Ideas

- WorktreeDescriptor, GitWorktreeProvider, and SessionEntity.WorktreePath already exist as stubs from Phase 1/2 — replace stub implementations with real git worktree operations
- StartSessionRequest.WorktreeId field already in contracts — use this to signal worktree mode
- Phase 7 already captures GitVersion in host inventory — read this for compatibility checking
- HostCommandProtocol already handles start-session commands — extend for worktree setup step before agent launch
- Stash before cleanup ensures no agent work is silently lost even when agents don't commit properly

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- **GitWorktreeProvider**: Stub implementation in `src/AgentHub.Orchestration/Storage/` — needs real git worktree commands
- **WorktreeDescriptor**: Record in Contracts (WorktreeId, RepoUrl, Ref, Shallow, Sparse, SparsePaths) — extend or adapt
- **SessionEntity**: Already has WorktreePath and CleanupPolicy columns — ready to use
- **StartSessionRequest**: Already has WorktreeId parameter — triggers worktree mode
- **HostCommandProtocol**: SSH command protocol with CreateStartSession — extend for worktree lifecycle
- **SshBackend**: Session lifecycle via SSH — add worktree create/cleanup steps
- **HostInventory.GitVersion**: Available from Phase 7 — read for worktree support verification

### Established Patterns
- SSH commands via HostCommandProtocol single-line JSON protocol
- OS-specific command construction (Windows-first, Linux/macOS support)
- Background services for polling/cleanup (HostMetricPollingService, HostInventoryPollingService)
- EntityMappers for bidirectional entity/DTO mapping
- Spectre.Console tables in CLI commands
- MudBlazor expandable cards in web UI (HostSidebar pattern)

### Integration Points
- **SshBackend.LaunchAsync**: Add worktree creation before agent start, cleanup after stop
- **SessionCoordinator**: Wire --worktree flag through to backend
- **Program.cs API endpoints**: Add `ah session diff` endpoint and `ah worktree cleanup` endpoint
- **SessionWatchCommand**: Show diff summary after session completes
- **SessionDetail.razor**: Add diff stats panel for completed worktree sessions
- **LaunchDialog.razor**: Add worktree toggle in session launch UI

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 09-git-worktree-isolation*
*Context gathered: 2026-03-10*
