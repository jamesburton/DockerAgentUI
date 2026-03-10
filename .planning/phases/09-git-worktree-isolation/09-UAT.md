---
status: complete
phase: 09-git-worktree-isolation
source: 09-01-SUMMARY.md, 09-02-SUMMARY.md
started: 2026-03-10T15:00:00Z
updated: 2026-03-10T15:20:00Z
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
  reason: "User reported: SessionMonitorService crashes with System.NotSupportedException: SQLite does not support expressions of type 'DateTimeOffset' in ORDER BY clauses. HostMetricPollingService and HostInventoryPollingService fail with SocketException for local-ssh host."
  severity: blocker
  test: 1
  artifacts: []
  missing: []

- truth: "Session starts successfully with worktree, host creates git worktree, session detail shows branch name"
  status: failed
  reason: "User reported: There is no sign of anything actually happening within the session, as it doesn't create any worktrees nor provide any session details (not even the trigger prompt)."
  severity: blocker
  test: 3
  artifacts: []
  missing: []

- truth: "Diff Stats panel shows files changed with added/removed line counts after session completes"
  status: failed
  reason: "User reported: Diff Stats panel shows 'Session still running -- diff stats available after completion' and stays like this indefinitely as no session appears to be running."
  severity: major
  test: 4
  artifacts: []
  missing: []

- truth: "CLI session diff displays diff stats for a worktree session"
  status: failed
  reason: "User reported: Session list shows session but session diff returns 'Error: Session ssh_40d6 not found.'"
  severity: major
  test: 6
  artifacts: []
  missing: []

- truth: "host set-repo sets default repo path and subsequent sessions work normally"
  status: failed
  reason: "User reported: Command runs but after setting repo path, launching a session without worktree details gives 400 Bad Request. Design concern: hosts should operate across projects, repo should be selected per-session not defaulted per-host."
  severity: major
  test: 8
  artifacts: []
  missing: []

- truth: "CLI worktree flags start a session with worktree isolation"
  status: failed
  reason: "User reported: CLI flags exist and parse correctly but starting a worktree session returns 500 Internal Server Error."
  severity: blocker
  test: 9
  artifacts: []
  missing: []
