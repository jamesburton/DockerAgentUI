# Roadmap: AgentSafeEnv

## Milestones

- v1.0 AgentSafeEnv -- Phases 1-6 (shipped 2026-03-09)
- v1.1 Multi-Agent & Interactive -- Phases 7-10 (in progress)

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

<details>
<summary>v1.0 AgentSafeEnv (Phases 1-6) -- SHIPPED 2026-03-09</summary>

- [x] Phase 1: Foundation and Event Infrastructure (3/3 plans) -- completed 2026-03-08
- [x] Phase 2: Session Orchestration and Agent Execution (5/5 plans) -- completed 2026-03-08
- [x] Phase 3: CLI Client (3/3 plans) -- completed 2026-03-09
- [x] Phase 4: Web Dashboard (3/3 plans) -- completed 2026-03-09
- [x] Phase 5: History API Contract Alignment (2/2 plans) -- completed 2026-03-09
- [x] Phase 6: Client Wiring and Polish (3/3 plans) -- completed 2026-03-09

Full details: `.planning/milestones/v1.0-ROADMAP.md`

</details>

### v1.1 Multi-Agent & Interactive

**Milestone Goal:** Enable interactive agent steering, multi-agent coordination across machines, host inventory discovery, and git worktree isolation for parallel sessions.

- [x] **Phase 7: Infrastructure & Host Inventory** - Schema migrations, metric cache, and SSH-based host tool discovery (completed 2026-03-10)
- [ ] **Phase 8: Interactive Session Steering** - Send follow-up instructions to running agents with delivery confirmation
- [ ] **Phase 9: Git Worktree Isolation** - Per-session worktree creation, cleanup, and diff visibility
- [ ] **Phase 10: Multi-Agent Coordination** - Sub-agent spawning, parent-child tracking, and resource-aware placement

## Phase Details

### Phase 7: Infrastructure & Host Inventory
**Goal**: Operators know exactly what is installed on every host before dispatching work
**Depends on**: Phase 6 (v1.0 complete)
**Requirements**: INFRA-01, INFRA-02, INVT-01, INVT-02, INVT-03, INVT-04
**Success Criteria** (what must be TRUE):
  1. Database schema includes ParentSessionId, DispatchId, and InventoryJson columns without breaking existing data
  2. System discovers which agent CLIs are installed on each registered host and displays versions in the dashboard
  3. System reports available disk space per host alongside existing CPU/memory metrics
  4. Inventory results are cached and the operator can force a refresh from the UI or API without waiting for the next poll cycle
**Plans**: 3 plans

Plans:
- [x] 07-01-PLAN.md -- Schema migration, HostMetricCache, contract types, agents.json config
- [x] 07-02-PLAN.md -- HostInventoryPollingService with SSH probing, API endpoints, cache wiring
- [x] 07-03-PLAN.md -- Web dashboard expandable host cards and CLI inventory columns

### Phase 8: Interactive Session Steering
**Goal**: Operators can send follow-up instructions to a running agent session and know the command was delivered
**Depends on**: Phase 7
**Requirements**: INTER-01, INTER-02, INTER-03
**Success Criteria** (what must be TRUE):
  1. User can type a follow-up instruction in CLI or Blazor UI and it reaches the running agent session mid-task
  2. Follow-up instructions are visually distinct from the original prompt in both CLI and web dashboard
  3. Coordinator displays delivery confirmation after the host daemon acknowledges receipt of the steering command
**Plans**: 3 plans

Plans:
- [ ] 08-01-PLAN.md -- Steering contracts, protocol, backend pipeline, and delivery confirmation
- [ ] 08-02-PLAN.md -- CLI steering event rendering, IsFollowUp flag, rapid-fire warning
- [ ] 08-03-PLAN.md -- Web UI steering rendering, delivery snackbar, fleet-wide steering visibility

### Phase 9: Git Worktree Isolation
**Goal**: Each agent session operates in its own git worktree so parallel agents on the same repo cannot conflict
**Depends on**: Phase 7 (needs inventory for git version verification)
**Requirements**: WKTREE-01, WKTREE-02, WKTREE-03, WKTREE-04
**Success Criteria** (what must be TRUE):
  1. Launching a session with worktree mode creates a new git worktree on the remote host before the agent starts
  2. Worktree and its branch are automatically cleaned up when the session ends (normal stop or force-kill)
  3. Worktree branches are named based on session ID and prompt summary so operators can identify them in git
  4. User can view git diff stats for a completed worktree session to assess merge-readiness
**Plans**: TBD

Plans:
- [ ] 09-01: TBD

### Phase 10: Multi-Agent Coordination
**Goal**: Operators can dispatch work across multiple machines with automatic placement and parent-child session tracking
**Depends on**: Phase 8, Phase 9
**Requirements**: COORD-01, COORD-02, COORD-03, COORD-04, COORD-05
**Success Criteria** (what must be TRUE):
  1. A running session can spawn child sessions on other hosts via the coordinator API
  2. Parent-child session relationships are visible in the dashboard and queryable via API
  3. Events from child sessions appear on the parent session's SSE stream
  4. Placement engine selects hosts using weighted scoring of CPU, memory, and active session count
  5. Sub-agent spawning enforces configurable depth and count limits, rejecting requests that exceed them
**Plans**: TBD

Plans:
- [ ] 10-01: TBD
- [ ] 10-02: TBD

## Progress

**Execution Order:**
Phases execute in numeric order: 7 -> 8 -> 9 -> 10

| Phase | Milestone | Plans Complete | Status | Completed |
|-------|-----------|----------------|--------|-----------|
| 1. Foundation and Event Infrastructure | v1.0 | 3/3 | Complete | 2026-03-08 |
| 2. Session Orchestration and Agent Execution | v1.0 | 5/5 | Complete | 2026-03-08 |
| 3. CLI Client | v1.0 | 3/3 | Complete | 2026-03-09 |
| 4. Web Dashboard | v1.0 | 3/3 | Complete | 2026-03-09 |
| 5. History API Contract Alignment | v1.0 | 2/2 | Complete | 2026-03-09 |
| 6. Client Wiring and Polish | v1.0 | 3/3 | Complete | 2026-03-09 |
| 7. Infrastructure & Host Inventory | v1.1 | 3/3 | Complete | 2026-03-10 |
| 8. Interactive Session Steering | v1.1 | 0/3 | Planning complete | - |
| 9. Git Worktree Isolation | v1.1 | 0/0 | Not started | - |
| 10. Multi-Agent Coordination | v1.1 | 0/0 | Not started | - |
