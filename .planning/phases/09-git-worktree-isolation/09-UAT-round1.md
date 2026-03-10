---
status: diagnosed
phase: 09-git-worktree-isolation
source: 09-01-SUMMARY.md, 09-02-SUMMARY.md
started: 2026-03-10T15:00:00Z
updated: 2026-03-10T15:30:00Z
---

## Current Test

[testing complete]

## Tests

### 1. Cold Start Smoke Test
expected: Kill any running server/service. Start the application from scratch. Server boots without errors, migrations complete, and homepage loads with live data.
result: issue
reported: "SessionMonitorService crashes with System.NotSupportedException: SQLite does not support expressions of type 'DateTimeOffset' in ORDER BY clauses. HostMetricPollingService and HostInventoryPollingService fail with SocketException for local-ssh host."
severity: blocker

### 2. Launch Dialog — Worktree Toggle
expected: Opening the launch dialog shows a "Worktree" toggle/checkbox. When enabled, a "Keep Branch" toggle and "Repo Path" text field appear.
result: pass

### 3. Start Session with Worktree
expected: Start a session with worktree enabled and a valid repo path. Session starts successfully. The host creates a git worktree (visible via SSH or logs). Session detail shows the worktree branch name.
result: issue
reported: "There is no sign of anything actually happening within the session, as it doesn't create any worktrees nor provide any session details (not even the trigger prompt)."
severity: blocker

### 4. Session Detail — Diff Stats Panel
expected: On session detail page for a worktree session that has made changes, an expandable "Diff Stats" panel shows files changed with added/removed line counts.
result: issue
reported: "Diff Stats panel shows 'Session still running -- diff stats available after completion' and stays like this indefinitely as no session appears to be running."
severity: major

### 5. Session Stop — Worktree Cleanup
expected: Stopping a worktree session (without keep-branch) cleans up the worktree directory and deletes the branch on the host. Stopping with keep-branch preserves the branch.
result: skipped
reason: Unable to test — worktree creation not working yet

### 6. CLI — session diff Command
expected: Running `ah session diff <sessionId>` displays diff stats (files changed, lines added/removed) for a worktree session.
result: issue
reported: "Session list shows session but session diff returns 'Error: Session ssh_40d6 not found.'"
severity: major

### 7. CLI — worktree cleanup Command
expected: Running `ah worktree cleanup <hostId>` removes orphaned worktrees on the specified host.
result: skipped
reason: Unable to test — worktree creation not working yet

### 8. CLI — host set-repo Command
expected: Running `ah host set-repo <hostId> <path>` sets the default repo path for that host. Subsequent sessions on that host use this path by default.
result: issue
reported: "Command runs but after setting repo path, launching a session without worktree details gives 400 Bad Request. Design concern: hosts should operate across projects, repo should be selected per-session not defaulted per-host."
severity: major

### 9. CLI — Start with Worktree Flags
expected: Running `ah session start --worktree --repo-path /path/to/repo` starts a session with worktree isolation in the specified repo. Adding `--keep-branch` preserves the branch on stop.
result: issue
reported: "CLI flags exist and parse correctly but starting a worktree session returns 500 Internal Server Error."
severity: blocker

## Summary

total: 9
passed: 1
issues: 6
pending: 0
skipped: 2

## Gaps

- truth: "Server boots without errors, migrations complete, and homepage loads with live data"
  status: failed
  reason: "User reported: SessionMonitorService crashes with System.NotSupportedException: SQLite does not support expressions of type 'DateTimeOffset' in ORDER BY clauses."
  severity: blocker
  test: 1
  root_cause: "SessionMonitorService.cs:80 — OrderByDescending(e => e.TsUtc) on DateTimeOffset column that SQLite EF Core provider cannot translate in ORDER BY"
  artifacts:
    - path: "src/AgentHub.Orchestration/Monitoring/SessionMonitorService.cs"
      issue: "OrderByDescending on DateTimeOffset column at line 80"
  missing:
    - "Use AsEnumerable() before OrderByDescending to perform ordering in memory after WHERE filter"
  debug_session: ".planning/debug/sqlite-datetimeoffset-orderby.md"

- truth: "Session starts successfully with worktree, host creates git worktree, session detail shows branch name"
  status: failed
  reason: "User reported: There is no sign of anything actually happening within the session, as it doesn't create any worktrees nor provide any session details (not even the trigger prompt)."
  severity: blocker
  test: 3
  root_cause: "Two failures: (1) LaunchDialog worktree toggle doesn't auto-enable AcceptRisk, so SimplePlacementEngine:31-32 rejects with 400 that UI swallows silently. (2) WorktreeService.CreateWorktreeAsync:22-31 never checks git command output — failures silently swallowed."
  artifacts:
    - path: "src/AgentHub.Web/Components/Shared/LaunchDialog.razor"
      issue: "Worktree toggle independent of AcceptRisk — SSH requires AcceptRisk but worktree doesn't auto-set it"
    - path: "src/AgentHub.Orchestration/Worktree/WorktreeService.cs"
      issue: "CreateWorktreeAsync lines 22-31 ignores git command exit status/output"
    - path: "src/AgentHub.Orchestration/Placement/SimplePlacementEngine.cs"
      issue: "Lines 31-32 hard gate: SSH requires AcceptRisk with no worktree bypass"
  missing:
    - "Auto-enable AcceptRisk when worktree is toggled on (SSH is required for worktree)"
    - "Check git worktree add output for errors and throw InvalidOperationException on failure"
  debug_session: ".planning/debug/worktree-session-silent-fail.md"

- truth: "Diff Stats panel shows files changed with added/removed line counts after session completes"
  status: failed
  reason: "User reported: Diff Stats panel shows 'Session still running -- diff stats available after completion' and stays like this indefinitely."
  severity: major
  test: 4
  root_cause: "SessionDetail.razor SSE loop (lines 174-189) exits silently when stream ends without refreshing session state. _session.State stays Running permanently, blocking diff stats panel at line 79."
  artifacts:
    - path: "src/AgentHub.Web/Components/Pages/SessionDetail.razor"
      issue: "SSE loop Task.Run exits without final state refresh; lines 79 and 298 guard on stale Running state"
  missing:
    - "Add final GetSessionAsync + StateHasChanged after SSE ReadAllAsync loop completes"
  debug_session: ".planning/debug/diff-stats-still-running.md"

- truth: "CLI session diff displays diff stats for a worktree session"
  status: failed
  reason: "User reported: Session list shows session but session diff returns 'Error: Session ssh_40d6 not found.'"
  severity: major
  test: 6
  root_cause: "session list truncates IDs to 8 chars (Program.cs:119,527) but diff endpoint (Program.cs:288) does exact == match. User copies truncated ID which doesn't match full ID in DB."
  artifacts:
    - path: "src/AgentHub.Cli/Program.cs"
      issue: "Lines 119, 527 truncate session ID to 8 chars in list display"
    - path: "src/AgentHub.Service/Program.cs"
      issue: "Line 288 diff endpoint uses exact SessionId == match"
  missing:
    - "Add prefix matching (StartsWith) fallback when exact match fails on session lookup endpoints"
  debug_session: ""

- truth: "host set-repo sets default repo path and subsequent sessions work normally"
  status: failed
  reason: "User reported: Command runs but launching session without worktree details gives 400 Bad Request."
  severity: major
  test: 8
  root_cause: "SshBackend.CanHandle (lines 58-62) requires ExecutionMode==Ssh && AcceptRisk, but placement engine routes Auto mode to SSH hosts when --host is specified. Mismatch causes CanHandle to reject → 400."
  artifacts:
    - path: "src/AgentHub.Orchestration/Backends/SshBackend.cs"
      issue: "CanHandle lines 58-62 requires ExecutionMode==Ssh && AcceptRisk"
    - path: "src/AgentHub.Orchestration/Placement/SimplePlacementEngine.cs"
      issue: "Lines 11-17 allows Auto+TargetHostId to select SSH nodes without AcceptRisk"
  missing:
    - "Relax CanHandle to accept Auto mode when placement already routed to SSH host"
  debug_session: ""

- truth: "CLI worktree flags start a session with worktree isolation"
  status: failed
  reason: "User reported: CLI flags exist and parse correctly but starting a worktree session returns 500 Internal Server Error."
  severity: blocker
  test: 9
  root_cause: "Same as Test 3 root cause #2: WorktreeService.CreateWorktreeAsync silently swallows git failures. SSH exceptions are not InvalidOperationException so they fall through to generic 500 catch in Program.cs:221-225."
  artifacts:
    - path: "src/AgentHub.Orchestration/Worktree/WorktreeService.cs"
      issue: "CreateWorktreeAsync ignores git command exit status"
    - path: "src/AgentHub.Service/Program.cs"
      issue: "Lines 221-225 generic catch returns bare 500 for non-InvalidOperationException"
  missing:
    - "Check git command output for errors in WorktreeService"
    - "Wrap SSH exceptions as InvalidOperationException or add specific catch clauses"
  debug_session: ".planning/debug/worktree-session-silent-fail.md"
