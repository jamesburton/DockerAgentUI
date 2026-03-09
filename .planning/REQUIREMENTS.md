# Requirements: AgentSafeEnv

**Defined:** 2026-03-09
**Core Value:** See what every agent is doing across every machine in real-time, and deploy new agent sessions to any registered host from one place.

## v1.1 Requirements

Requirements for milestone v1.1 Multi-Agent & Interactive. Each maps to roadmap phases.

### Interactive Sessions

- [ ] **INTER-01**: User can send follow-up instructions to a running agent session mid-task
- [ ] **INTER-02**: CLI and Blazor UI visually distinguish initial prompt from follow-up steering
- [ ] **INTER-03**: Coordinator receives acknowledgment from host daemon confirming command delivery

### Host Inventory

- [ ] **INVT-01**: System discovers installed agent CLIs on each host via SSH probing
- [ ] **INVT-02**: System detects agent CLI versions to prevent incompatible flag dispatch
- [ ] **INVT-03**: System monitors available disk space on hosts via health polling
- [ ] **INVT-04**: Inventory results are cached with configurable TTL and refreshable on demand

### Git Worktrees

- [ ] **WKTREE-01**: System creates a git worktree on the remote host before launching an agent session
- [ ] **WKTREE-02**: System cleans up worktree and branch when session ends (stop or force-kill)
- [ ] **WKTREE-03**: Worktree branches are auto-named based on session ID and prompt summary
- [ ] **WKTREE-04**: User can view git diff stats for a completed worktree session (merge-readiness)

### Multi-Agent Coordination

- [ ] **COORD-01**: Running session can spawn child sessions on other hosts via coordinator API
- [ ] **COORD-02**: Parent-child session relationships are tracked in the database
- [ ] **COORD-03**: Child session events are visible on the parent's SSE stream
- [ ] **COORD-04**: Placement engine uses weighted scoring of CPU, memory, and session count
- [ ] **COORD-05**: Sub-agent spawning enforces depth and count limits to prevent cascades

### Schema & Infrastructure

- [ ] **INFRA-01**: EF Core migration adds ParentSessionId, DispatchId, InventoryJson columns
- [ ] **INFRA-02**: HostMetricCache provides real-time metrics to placement engine

## Future Requirements

Deferred to v1.2+. Tracked but not in current roadmap.

### Interactive Sessions

- **INTER-10**: User can pause a running agent session (SIGTSTP via SSH)
- **INTER-11**: User can resume a paused agent session (SIGCONT via SSH)
- **INTER-12**: System detects agent idle state and surfaces redirect opportunity

### Multi-Agent Coordination

- **COORD-10**: User can launch N sessions in a single batch API call
- **COORD-11**: Session dependency DAG with fan-out/fan-in execution
- **COORD-12**: Token/cost budget inheritance from parent to child sessions

### Host Inventory

- **INVT-10**: Dedicated health-check probes beyond CPU/memory/disk

## Out of Scope

| Feature | Reason |
|---------|--------|
| Agent-to-agent direct communication | Debugging nightmares, use orchestrator-mediated coordination |
| Automatic task decomposition | Unreliable, operator decomposes manually |
| Auto-merge of agent worktree results | Human-in-the-loop for merges |
| Automatic tool installation on hosts | Security risk, fragile across distros |
| Real-time token-by-token thought streaming | Bandwidth cost, agents don't expose cleanly |
| Idle detection and auto-redirect | Fragile pattern matching across agent types |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| INFRA-01 | — | Pending |
| INFRA-02 | — | Pending |
| INVT-01 | — | Pending |
| INVT-02 | — | Pending |
| INVT-03 | — | Pending |
| INVT-04 | — | Pending |
| INTER-01 | — | Pending |
| INTER-02 | — | Pending |
| INTER-03 | — | Pending |
| WKTREE-01 | — | Pending |
| WKTREE-02 | — | Pending |
| WKTREE-03 | — | Pending |
| WKTREE-04 | — | Pending |
| COORD-01 | — | Pending |
| COORD-02 | — | Pending |
| COORD-03 | — | Pending |
| COORD-04 | — | Pending |
| COORD-05 | — | Pending |

**Coverage:**
- v1.1 requirements: 18 total
- Mapped to phases: 0
- Unmapped: 18 ⚠️

---
*Requirements defined: 2026-03-09*
*Last updated: 2026-03-09 after initial definition*
