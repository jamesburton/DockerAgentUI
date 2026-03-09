# Roadmap: AgentSafeEnv

## Overview

AgentSafeEnv delivers a multi-agent orchestration control plane across four phases: first building the persistent data layer, coordinator API, and event streaming foundation; then wiring up real session execution with SSH backends, agent adapters, and policy enforcement; then exposing everything through the CLI as primary interface; and finally adding the Blazor web dashboard for visual fleet oversight. Each phase delivers a coherent, testable capability that builds on the previous.

## Phases

**Phase Numbering:**
- Integer phases (1, 2, 3): Planned milestone work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [ ] **Phase 1: Foundation and Event Infrastructure** - Persistent data layer, coordinator API, SSE event streaming, and agent adapter abstraction
- [x] **Phase 2: Session Orchestration and Agent Execution** - Full session lifecycle on remote hosts with SSH backend, policy enforcement, and approval flow (completed 2026-03-08)
- [ ] **Phase 3: CLI Client** - Primary user interface for launching, monitoring, and managing agent sessions
- [x] **Phase 4: Web Dashboard** - Blazor fleet overview with live session streaming and resource visibility (completed 2026-03-09)
- [ ] **Phase 5: History API Contract Alignment** - Fix history endpoint response shape and pagination (gap closure from audit)
- [ ] **Phase 6: Client Wiring and Polish** - Wire input endpoint to clients, populate host metrics, SSE incremental updates, minor fixes (gap closure from audit)

## Phase Details

### Phase 1: Foundation and Event Infrastructure
**Goal**: The platform has a working API with persistent storage, real-time event streaming, and the abstraction layer for plugging in agent types and execution backends
**Depends on**: Nothing (first phase)
**Requirements**: INFRA-01, INFRA-02, INFRA-03, MON-01, AGENT-01
**Success Criteria** (what must be TRUE):
  1. Coordinator API responds to health checks and serves REST endpoints for session CRUD and host listing
  2. Session and host data persists across coordinator restarts (no in-memory-only state)
  3. SSE endpoint streams events in real-time with event IDs and supports Last-Event-ID replay on reconnect
  4. Agent adapter interface exists with at least one concrete implementation (Claude Code) that can translate a session request into a CLI command
  5. Host daemon concept is defined with a receivable command protocol (even if initially stub/SSH-based)
**Plans:** 3/3 plans executed

Plans:
- [x] 01-01-PLAN.md — EF Core data layer, persistence, test project, API wiring
- [x] 01-02-PLAN.md — Durable event service with SSE streaming and Last-Event-ID replay
- [x] 01-03-PLAN.md — Agent adapter pattern (Claude Code) and host daemon command protocol

### Phase 2: Session Orchestration and Agent Execution
**Goal**: Users can launch, monitor, and stop real agent sessions on remote hosts with policy checks, input sanitization, and approval gating for destructive actions
**Depends on**: Phase 1
**Requirements**: SESS-01, SESS-02, SESS-03, SESS-04, SESS-05, AGENT-02, AGENT-03, AGENT-04, AGENT-05
**Success Criteria** (what must be TRUE):
  1. User can launch an agent session on a registered remote host and see it appear in the session list
  2. User can stop a running session both gracefully and via force-kill
  3. Fire-and-forget task sessions run to completion without requiring ongoing connection
  4. Past session history is retrievable with stored output and final outcome
  5. Destructive agent actions trigger an approval prompt; permission-skip flags bypass it when set
**Plans:** 5/5 plans complete

Plans:
- [x] 02-01-PLAN.md — Data model extensions, multi-format config loader (JSON/YAML/MD), trust tiers, permission wiring
- [x] 02-02-PLAN.md — SSH backend rewrite with SSH.NET, session monitor service, heartbeat/orphan detection
- [x] 02-03-PLAN.md — Approval/elevation gating service with SSE delivery, extended sanitization with trust tiers
- [x] 02-04-PLAN.md — Coordinator wiring, API endpoints for force-kill, approval resolution, session history
- [ ] 02-05-PLAN.md — Gap closure: wire ApprovalService into SessionCoordinator, create ConfigResolutionService pipeline

### Phase 3: CLI Client
**Goal**: A command-line tool serves as the primary interface for all platform operations, with both interactive and scriptable modes
**Depends on**: Phase 2
**Requirements**: CLI-01, CLI-02, MON-02, MON-03
**Success Criteria** (what must be TRUE):
  1. User can launch, list, stop, and view logs of sessions entirely from the CLI
  2. CLI supports non-interactive/scriptable mode (exit codes, machine-readable output) for automation
  3. User can view per-host resource usage (CPU, memory) from the CLI
  4. User receives visible notifications in the CLI for session completion, errors, and approval requests
**Plans:** 3 plans

Plans:
- [x] 03-01-PLAN.md — CLI project scaffold, API client, output formatters, CRUD commands (session start/list/stop, host list/status, config show)
- [x] 03-02-PLAN.md — SSE streaming, live watch displays, notification service, approval inline handler, listen command
- [ ] 03-03-PLAN.md — Gap closure: wire ApprovalPromptHandler into session watch, remove orphaned LiveDisplayManager

### Phase 4: Web Dashboard
**Goal**: A Blazor web dashboard provides visual fleet oversight with live session status and real-time output streaming
**Depends on**: Phase 2
**Requirements**: WEB-01, WEB-02
**Success Criteria** (what must be TRUE):
  1. Web dashboard shows a fleet overview page listing all hosts and their current session status
  2. User can click into a session and see real-time agent output streamed inline
  3. Dashboard updates live without manual refresh (SSE-driven)
**Plans:** 3/3 plans complete

Plans:
- [ ] 04-01-PLAN.md — Project scaffold, MudBlazor setup, DashboardApiClient, SseStreamService, Aspire wiring, layout with dark theme, unit tests
- [ ] 04-02-PLAN.md — Fleet overview page with host sidebar, session data table, polling/SSE toggle, launch dialog, stop capability
- [ ] 04-03-PLAN.md — Session detail page with terminal output panel, live streaming, history replay, approval handling

### Phase 5: History API Contract Alignment
**Goal**: Session history API returns correctly shaped responses with working pagination, so CLI and Web clients can display full event metadata and paginate results
**Depends on**: Phase 1, Phase 2, Phase 3, Phase 4
**Requirements**: SESS-05, CLI-01, WEB-02
**Gap Closure**: Closes INT-01, INT-02 from v1.0 audit
**Success Criteria** (what must be TRUE):
  1. `/api/sessions/{id}/history` returns `SessionEvent` DTOs via `EntityMappers.ToDto()` (not anonymous objects)
  2. History endpoint binds `page`, `pageSize`, and optional `kind` filter query parameters
  3. CLI `ah session logs <id>` shows event metadata (Meta dictionary populated, not null)
  4. Web SessionDetail history replay shows event metadata correctly
  5. Integration tests verify response shape matches `SessionEvent` contract
**Plans:** 0/0

### Phase 6: Client Wiring and Polish
**Goal**: All API endpoints have client callers, host resource metrics are populated, and minor quality issues are resolved
**Depends on**: Phase 5
**Requirements**: AGENT-03, AGENT-04, MON-02
**Gap Closure**: Closes INT-03 from v1.0 audit + tech debt items
**Success Criteria** (what must be TRUE):
  1. CLI and Web clients expose `SendInputAsync` method calling `POST /api/sessions/{id}/input`
  2. CLI has `ah session input <id> <text>` command for sending input to running sessions
  3. Web SessionDetail page has an input panel for sending text to running sessions
  4. Host resource metrics (CPU, memory) are populated from SSH status reports and display real values
  5. FleetOverview SSE updates incrementally (patch state) rather than full reload
  6. CS8602 nullable warning in SseStreamReader.cs resolved
  7. API documentation/spec alignment: approval endpoint path matches implementation
**Plans:** 0/0

## Progress

**Execution Order:**
Phases execute in numeric order: 1 -> 2 -> 3 -> 4 -> 5 -> 6

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Foundation and Event Infrastructure | 3/3 | Complete | 2026-03-08 |
| 2. Session Orchestration and Agent Execution | 5/5 | Complete   | 2026-03-08 |
| 3. CLI Client | 2/3 | In Progress | - |
| 4. Web Dashboard | 3/3 | Complete   | 2026-03-09 |
| 5. History API Contract Alignment | 0/0 | Pending | - |
| 6. Client Wiring and Polish | 0/0 | Pending | - |
