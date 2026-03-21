---
status: awaiting_human_verify
trigger: "Investigate why starting a session with worktree enabled does nothing - no worktree created, no session details shown, no trigger prompt. Test 3: Web UI launch with worktree toggle, Test 9: CLI session start --worktree --repo-path /path -> 500"
created: 2026-03-10T00:00:00Z
updated: 2026-03-10T00:00:00Z
---

## Current Focus

hypothesis: Two root causes - (1) SshBackend.CanHandle requires AcceptRisk=true but LaunchDialog allows worktree without risk, and (2) the catch-all in Program.cs line 221-225 swallows the real error as a generic 500
test: Trace CanHandle logic and exception flow
expecting: CanHandle returns false when AcceptRisk=false, causing "Backend ssh cannot handle" error
next_action: Document findings

## Symptoms

expected: Worktree session creates a git worktree on the remote host, session starts with agent in that worktree
actual: Web UI - nothing happens (silent failure); CLI - 500 Internal Server Error
errors: 500 Internal Server Error from CLI; Web UI shows "Launch failed" snackbar
reproduction: Enable worktree toggle in LaunchDialog, launch session; or CLI `session start --worktree --repo-path /path`
started: Since worktree feature was implemented

## Eliminated

(none)

## Evidence

- timestamp: 2026-03-10T00:01:00Z
  checked: SshBackend.CanHandle (line 58-62)
  found: Requires AcceptRisk=true AND node.AllowRiskyDirectExec=true. Worktree sessions don't inherently need risk acceptance but the gate blocks them.
  implication: If user enables worktree but not "Skip permission prompts", CanHandle returns false

- timestamp: 2026-03-10T00:02:00Z
  checked: SessionCoordinator.StartSessionAsync (line 50-51)
  found: After placement selects a node, it calls CanHandle. If false, throws InvalidOperationException("Backend ssh cannot handle the selected request.")
  implication: This is caught by Program.cs line 211 as InvalidOperationException -> returns 400 BadRequest, not 500

- timestamp: 2026-03-10T00:03:00Z
  checked: SimplePlacementEngine.ChooseNode (line 31-32)
  found: If ExecutionMode==Ssh and AcceptRisk==false, throws InvalidOperationException("SSH execution requires AcceptRisk=true.")
  implication: This fires BEFORE CanHandle even runs. Caught as 400 by Program.cs. But the Web UI shows this as "Launch failed" snackbar which the user may interpret as "nothing happens"

- timestamp: 2026-03-10T00:04:00Z
  checked: LaunchDialog.razor Launch() method (line 119-131)
  found: WorktreeId is set to a new GUID when _useWorktree is true. AcceptRisk comes from a separate checkbox. User can enable worktree WITHOUT enabling AcceptRisk.
  implication: The two checkboxes are independent - worktree does not auto-enable AcceptRisk

- timestamp: 2026-03-10T00:05:00Z
  checked: Program.cs POST /api/sessions (line 204-226)
  found: InvalidOperationException -> 400 BadRequest. Generic Exception -> 500. SocketException -> 502. The 500 from CLI means the error is NOT an InvalidOperationException.
  implication: The CLI 500 must be hitting the generic catch on line 221-225, meaning something OTHER than InvalidOperationException is thrown

- timestamp: 2026-03-10T00:06:00Z
  checked: SshBackend.StartAsync worktree creation block (line 119-134)
  found: If WorktreeId is non-empty AND repoRoot can't be determined, throws InvalidOperationException (caught as 400). If SSH connection fails, that's also InvalidOperationException (caught as 400). But if connection.ExecuteCommandAsync fails mid-worktree-creation, that could be an unhandled exception type.
  implication: The 500 likely comes from SSH connection/command execution failures that aren't InvalidOperationException

## Resolution

root_cause: |
  TWO DISTINCT ROOT CAUSES:

  ROOT CAUSE 1 (Test 3 - Web UI "nothing happens"):
  The LaunchDialog allows enabling worktree without enabling "Skip permission prompts" (AcceptRisk).
  But the SSH execution path has TWO gates that require AcceptRisk=true:
    - SimplePlacementEngine.cs line 31-32: throws if ExecutionMode==Ssh && !AcceptRisk
    - SshBackend.CanHandle line 58-62: requires AcceptRisk=true
  When the user enables worktree but not AcceptRisk, the placement engine rejects immediately.
  The error message "SSH execution requires AcceptRisk=true" is returned as a 400 BadRequest,
  which the LaunchDialog shows as "Launch failed: SSH execution requires AcceptRisk=true" in a
  snackbar -- but users may miss this or interpret it as "nothing happened" since there's no
  navigation or visible session created.

  ROOT CAUSE 2 (Test 9 - CLI 500 error):
  When AcceptRisk IS true and the SSH connection is attempted, failures during worktree creation
  (connection.ExecuteCommandAsync) can throw exception types that are NOT InvalidOperationException
  (e.g., SshException, TimeoutException, IOException from SSH.NET). These fall through to the
  generic catch on Program.cs line 221-225 which returns a 500 with a generic message. The actual
  SSH host may not be reachable, or the git worktree command fails, but the error is swallowed
  into a generic 500 instead of a meaningful error.

  Additionally, WorktreeService.CreateWorktreeAsync (line 22-31) does NOT check the output of the
  git worktree command for errors. It runs the command, logs success unconditionally, and returns
  the path -- even if the git command failed. This means a failed worktree creation is silently
  ignored, and the session proceeds with a non-existent worktree path.

fix: |
  Three changes applied:

  1. LaunchDialog.razor: Disabled the AcceptRisk checkbox when worktree is enabled (Disabled="_useWorktree"),
     added caption explaining why it's required, and added defense-in-depth guard in Launch() to re-enforce
     AcceptRisk=true when worktree is on (in case state gets out of sync).

  2. Program.cs POST /api/sessions: Added explicit catch blocks for TimeoutException (504) and IOException (502)
     so SSH connection failures return meaningful HTTP status codes and error messages instead of generic 500.

  3. WorktreeService.CreateWorktreeAsync already had error checking (lines 29-33 check for "fatal:" and "error:"
     in git output). No change needed -- the original diagnosis was incorrect about this being missing.

verification: |
  - AgentHub.Orchestration project builds with 0 errors, 0 warnings
  - AgentHub.Service project compiles with 0 CS/RZ errors (only MSB file-lock warnings from running process)
  - AgentHub.Web project compiles with 0 CS/RZ errors (only MSB file-lock warnings from running process)
  - Human verification of UI behavior needed

files_changed:
  - src/AgentHub.Web/Components/Shared/LaunchDialog.razor
  - src/AgentHub.Service/Program.cs
