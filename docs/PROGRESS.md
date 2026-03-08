# Progress

## Done in this archive

- Expanded shared models for:
  - execution mode
  - risk level
  - skills
  - policy snapshots
  - shared worktrees
- Added orchestration abstractions for:
  - host registry
  - session coordinator
  - sanitization
  - skill registry and policy
  - blob/shared storage
  - git worktree materialization
- Added service endpoints for:
  - hosts
  - skills
  - policy
  - sessions CRUD-ish
  - SSE events
- Added config samples for:
  - hosts
  - policies
  - sanitizers
  - builtin and custom skills
- Added SSH backend starter and refined in-memory Nomad-like backend

## Not done yet

- no verified compilation in this environment
- no real Nomad API integration
- no real SSH transport or command execution
- no persistence/database
- no role-aware auth
- no MAUI admin UX for policy editing
- no blob cloud provider integration yet

## Suggested next commits

1. Make service build and run on local dev machine.
2. Add real config binding + reload notifications.
3. Implement SSH transport:
   - key auth
   - command runner
   - log tailing
4. Implement Nomad transport:
   - job spec writer
   - allocations polling/logs
5. Add tests for sanitizer + policy + placement.
