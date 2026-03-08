# Requirements: AgentSafeEnv

**Defined:** 2026-03-08
**Core Value:** See what every agent is doing across every machine in real-time, and deploy new agent sessions to any registered host from one place.

## v1 Requirements

Requirements for initial release. Each maps to roadmap phases.

### Session Management

- [x] **SESS-01**: User can launch an agent session on a registered remote host
- [x] **SESS-02**: User can view status of all running sessions across all hosts
- [x] **SESS-03**: User can stop a running session (graceful + force-kill)
- [x] **SESS-04**: User can launch a fire-and-forget task session that runs to completion
- [x] **SESS-05**: User can review past session history with stored output and outcome

### Monitoring & Streaming

- [x] **MON-01**: User can stream real-time agent output via SSE as it happens
- [x] **MON-02**: User can view resource usage (CPU, memory) per host
- [x] **MON-03**: User receives notifications for session completion, errors, and approval requests

### Agent Support

- [x] **AGENT-01**: System supports multiple agent types via adapter pattern (Claude, Codex, Copilot, Gemini, OpenCode)
- [x] **AGENT-02**: Skills and policies are defined via configuration files (YAML/JSON)
- [x] **AGENT-03**: Inputs to agents pass through a configurable sanitization layer
- [x] **AGENT-04**: Destructive agent actions trigger an approval/elevation flow requiring human confirmation
- [x] **AGENT-05**: User can set permission-skip flags (e.g., `--dangerously-skip-permissions`) per agent, especially when running in sandbox/container

### Infrastructure

- [x] **INFRA-01**: Coordinator exposes REST API for session CRUD, host listing, and event streaming
- [x] **INFRA-02**: Lightweight host daemon runs on each target machine to receive commands and report status
- [x] **INFRA-03**: Session state persists in a durable data store (EF Core with pluggable provider)

### Client Interfaces

- [x] **CLI-01**: CLI client supports launching, monitoring, and managing sessions
- [ ] **CLI-02**: CLI supports both interactive and scriptable (non-interactive) modes
- [ ] **WEB-01**: Blazor web dashboard shows fleet overview with live session status
- [ ] **WEB-02**: Web dashboard streams real-time agent output inline

## v2 Requirements

Deferred to future release. Tracked but not in current roadmap.

### Session Management

- **SESS-06**: User can run interactive sessions with bidirectional steering mid-run
- **SESS-07**: Multiple agents can coordinate on related tasks across machines with shared context

### Infrastructure

- **INFRA-04**: Host inventory with formal registration, agent tool discovery, and health checks
- **INFRA-05**: Git worktree-based isolation per agent session with auto-cleanup
- **INFRA-06**: Blob-backed artifact and snapshot storage for cross-host persistence
- **INFRA-07**: Nomad execution backend for scheduler-based session placement
- **INFRA-08**: Container-based execution environments for full isolation
- **INFRA-09**: MCP protocol support as additional agent control protocol

### Monitoring

- **MON-04**: Token spend tracking per session via agent CLI output parsing or API
- **MON-05**: Diff review workflow (view agent changes, approve/reject before merge)

### Client Interfaces

- **CLI-03**: MAUI desktop and mobile client for fleet management

## Out of Scope

| Feature | Reason |
|---------|--------|
| Multi-tenant / multi-user auth | Single operator for now -- adds auth, RBAC, isolation, billing complexity |
| Agent-to-agent direct communication | Debugging distributed agent conversations is nightmarish -- use orchestrator-mediated coordination |
| Auto-merge of agent PRs | Industry consensus is human-in-the-loop for merges -- auto-create PRs, operator merges |
| Natural language task decomposition | Unreliable quality -- operator decomposes tasks, system executes reliably |
| Automatic tool installation on hosts | Fragile and security concern -- detect what's installed, operator installs manually |
| Mobile-first UI design | Primary workflow needs screen real estate -- desktop-first, mobile-responsive as bonus |
| OpenClaw / NanoClaw agent support | Future iteration -- not enough information on these agents yet |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| SESS-01 | Phase 2 | Complete |
| SESS-02 | Phase 2 | Complete |
| SESS-03 | Phase 2 | Complete |
| SESS-04 | Phase 2 | Complete |
| SESS-05 | Phase 2 | Complete |
| MON-01 | Phase 1 | Complete |
| MON-02 | Phase 3 | Complete |
| MON-03 | Phase 3 | Complete |
| AGENT-01 | Phase 1 | Complete |
| AGENT-02 | Phase 2 | Complete |
| AGENT-03 | Phase 2 | Complete |
| AGENT-04 | Phase 2 | Complete |
| AGENT-05 | Phase 2 | Complete |
| INFRA-01 | Phase 1 | Complete |
| INFRA-02 | Phase 1 | Complete |
| INFRA-03 | Phase 1 | Complete |
| CLI-01 | Phase 3 | Complete |
| CLI-02 | Phase 3 | Pending |
| WEB-01 | Phase 4 | Pending |
| WEB-02 | Phase 4 | Pending |

**Coverage:**
- v1 requirements: 20 total
- Mapped to phases: 20
- Unmapped: 0

---
*Requirements defined: 2026-03-08*
*Last updated: 2026-03-08 after roadmap creation*
