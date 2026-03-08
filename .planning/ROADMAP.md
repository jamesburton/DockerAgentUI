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
- [ ] **Phase 4: Web Dashboard** - Blazor fleet overview with live session streaming and resource visibility

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
**Plans:** 2 plans

Plans:
- [ ] 03-01-PLAN.md — CLI project scaffold, API client, output formatters, CRUD commands (session start/list/stop, host list/status, config show)
- [ ] 03-02-PLAN.md — SSE streaming, live watch displays, notification service, approval inline handler, listen command

### Phase 4: Web Dashboard
**Goal**: A Blazor web dashboard provides visual fleet oversight with live session status and real-time output streaming
**Depends on**: Phase 2
**Requirements**: WEB-01, WEB-02
**Success Criteria** (what must be TRUE):
  1. Web dashboard shows a fleet overview page listing all hosts and their current session status
  2. User can click into a session and see real-time agent output streamed inline
  3. Dashboard updates live without manual refresh (SSE-driven)
**Plans**: TBD

Plans:
- [ ] 04-01: TBD

## Progress

**Execution Order:**
Phases execute in numeric order: 1 -> 2 -> 3 -> 4

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Foundation and Event Infrastructure | 3/3 | Complete | 2026-03-08 |
| 2. Session Orchestration and Agent Execution | 5/5 | Complete   | 2026-03-08 |
| 3. CLI Client | 0/2 | Not started | - |
| 4. Web Dashboard | 0/1 | Not started | - |
