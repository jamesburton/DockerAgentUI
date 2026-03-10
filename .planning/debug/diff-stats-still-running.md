---
status: diagnosed
trigger: "Diff Stats panel shows 'Session still running' indefinitely even when no session is running"
created: 2026-03-10T00:00:00Z
updated: 2026-03-10T00:00:00Z
---

## Current Focus

hypothesis: UI checks _session.State for Running/Pending but the state fetched from coordinator iterates backends which may return stale in-memory state, AND the SSE StateChanged handler refreshes _session but the condition on line 79 is never re-evaluated because the SSE loop exits before a final re-render with terminal state
test: Trace the data flow from SSE StateChanged event through _session refresh
expecting: _session.State never transitions to Stopped/Failed in the UI's local copy
next_action: confirmed - document root cause

## Symptoms

expected: After a session completes, expanding the Diff Stats panel should load and display diff statistics
actual: The panel perpetually shows "Session still running -- diff stats available after completion"
errors: none (no error, just wrong conditional branch)
reproduction: Open any completed session's detail page, expand Diff Stats panel
started: Likely since worktree diff feature was added

## Eliminated

(none needed - root cause found on first pass)

## Evidence

- timestamp: 2026-03-10T00:01:00Z
  checked: SessionState enum in Models.cs
  found: States are Pending=0, Running=1, Stopped=2, Failed=3 (no "Completed" state)
  implication: Terminal states are Stopped and Failed

- timestamp: 2026-03-10T00:02:00Z
  checked: SessionDetail.razor lines 79-81
  found: Guard condition is `_session.State is SessionState.Running or SessionState.Pending` -- shows "still running" message
  implication: If _session.State is anything other than Running/Pending, the diff should load

- timestamp: 2026-03-10T00:03:00Z
  checked: SessionCoordinator.GetSessionAsync (lines 28-37)
  found: Iterates ALL backends, returns first match. InMemoryBackend checked FIRST (registered first in DI).
  implication: If session was created via InMemoryBackend, it returns from in-memory dict. If via SshBackend, it queries DB.

- timestamp: 2026-03-10T00:04:00Z
  checked: InMemoryBackend.GetAsync (line 57-58)
  found: Returns from ConcurrentDictionary -- lost on service restart. Returns null if not found.
  implication: After restart, InMemoryBackend returns null, then SshBackend returns from DB -- this path works fine.

- timestamp: 2026-03-10T00:05:00Z
  checked: SessionDetail.razor OnInitializedAsync (lines 160-211)
  found: On page load, fetches session via API. If state is Running/Pending, subscribes to SSE. On StateChanged event (line 183-184), re-fetches session. This SHOULD update _session.State.
  implication: For live sessions that complete while user is watching, the state should update. But there's a critical issue...

- timestamp: 2026-03-10T00:06:00Z
  checked: SessionDetail.razor line 79 condition vs OnDiffPanelExpanded (lines 294-315)
  found: TWO separate checks: (1) line 79 in the Razor template renders the "still running" message, (2) line 298 in OnDiffPanelExpanded returns early if Running/Pending. Both check _session.State.
  implication: If _session.State correctly updates to Stopped/Failed, both should work. The problem must be in state NOT updating.

- timestamp: 2026-03-10T00:07:00Z
  checked: SSE subscription flow more carefully (lines 171-189)
  found: SSE subscription ONLY happens when state is Running/Pending at page load time (line 171). For sessions already completed when page is loaded, the else branch (line 192) loads history. In this case _session already has the terminal state from the initial GetSessionAsync call.
  implication: For already-completed sessions loaded fresh, _session.State SHOULD be Stopped/Failed. This should work... unless the API returns wrong state.

- timestamp: 2026-03-10T00:08:00Z
  checked: SessionCoordinator.GetSessionAsync backend iteration order
  found: _backends is built from DI: InMemoryBackend registered first (line 77 of Program.cs), SshBackend second (line 78). GetSessionAsync iterates in registration order.
  implication: InMemoryBackend.GetAsync is called first. If the nomad backend has a stale entry with Running state (e.g., the simulated session never transitions to Stopped because it only runs 5 ticks but never updates state to Stopped), it returns that stale Running state.

- timestamp: 2026-03-10T00:09:00Z
  checked: InMemoryBackend.StartAsync (lines 24-44)
  found: Creates session with State=Running. Spawns background task that emits 5 ticks then EXITS WITHOUT setting state to Stopped. The only way state changes is via StopAsync.
  implication: InMemoryBackend sessions that "complete" (background task finishes) remain in Running state forever in the in-memory dictionary. BUT this only affects nomad sessions, not SSH sessions.

- timestamp: 2026-03-10T00:10:00Z
  checked: The diff endpoint in Program.cs (lines 274-312)
  found: The API endpoint queries the DB DIRECTLY (line 288), bypassing the coordinator. It does NOT check session state at all -- it just needs WorktreeBranch to be set.
  implication: The API endpoint itself has no state guard. The problem is purely client-side in the Razor condition.

- timestamp: 2026-03-10T00:11:00Z
  checked: How SSH sessions complete and whether state reaches DB
  found: SshBackend.RunSessionAsync (line 432) calls UpdateSessionStateAsync -> sets entity.State = Stopped in DB. SshBackend.GetAsync reads from DB.
  implication: For SSH sessions, GetSessionAsync via coordinator should return correct terminal state from DB.

- timestamp: 2026-03-10T00:12:00Z
  checked: Re-examined the full flow for the REAL bug scenario
  found: The REAL issue is that the page may have been loaded WHILE the session was running, the SSE stream eventually ends (connection drops, session completes on host), but the StateChanged event may never arrive or the re-fetch may fail silently. In that case _session.State stays as Running in the component's local variable.
  implication: The _session object is only updated via (a) initial load, (b) SSE StateChanged event triggering re-fetch, (c) Stop/ForceStop button clicks. If the SSE stream breaks or the final StateChanged event is missed, the UI is stuck.

## Resolution

root_cause: |
  The UI component holds a local `_session` variable that is only refreshed in three scenarios:
  1. Initial page load (OnInitializedAsync)
  2. SSE StateChanged event received (triggers re-fetch)
  3. User clicks Stop/Force Stop (triggers re-fetch)

  If the SSE stream drops, times out, or the final StateChanged event is never emitted/received,
  `_session.State` remains `Running` in the component forever. The Razor template condition on
  line 79 (`_session.State is SessionState.Running or SessionState.Pending`) then permanently
  blocks the diff stats panel, showing "Session still running" indefinitely.

  Additionally, the `OnDiffPanelExpanded` handler on line 298 has its own early-return guard
  that also checks the same stale state, so even if the user collapses and re-expands the panel,
  the diff fetch is never attempted.

  Contributing factors:
  - No periodic polling to refresh session state
  - No SSE reconnection logic
  - When the SSE `ReadAllAsync` loop ends (line 178), no final state refresh is performed
  - The SSE loop catch only handles OperationCanceledException (line 188), so a broken connection
    that completes the enumerable silently exits without a final state update

fix: (not applied - diagnosis only)
verification: (not verified)
files_changed: []
