# Phase 2: Session Orchestration and Agent Execution - Context

**Gathered:** 2026-03-08
**Status:** Ready for planning

<domain>
## Phase Boundary

Users can launch, monitor, and stop real agent sessions on remote hosts with policy checks, input sanitization, and approval gating for destructive actions. Covers session lifecycle (start → monitor → stop), fire-and-forget task sessions, session history, skill/policy configuration, and the approval/elevation flow. CLI and web clients are separate phases — this phase builds the coordinator-side orchestration that they'll consume.

</domain>

<decisions>
## Implementation Decisions

### Session Launch Flow
- User provides either a prompt string OR a file reference (spec, PLAN.md, etc.) — agent receives whichever is given
- Coordinator auto-selects host via placement engine by default; user can pin a specific host with `--host` flag
- Fire-and-forget task sessions: host daemon pushes events back over SSH channel + coordinator sends periodic heartbeat pings to detect dead connections
- Each session gets its own git worktree on the remote host for isolation — prevents conflicts when multiple agents work on the same repo

### Approval & Elevation Gating
- Approval requests delivered as SSE events to all connected clients (CLI, web) via existing DurableEventService
- Approval timeout behavior is configurable at four scoping levels: default → host → project → task
  - Default: pause session indefinitely until someone responds
  - With `dangerously-skip-permissions`: auto-approve without prompting
  - Configurable timeout actions: continue with workarounds, auto-approve, or stop session
- Approval requests include detailed context — the specific action (command, file path, diff preview if available)
- Action-level trust tiers defined in policy config: always-allow (reads, ls), prompt (writes, deletes), always-deny (destructive operations)

### Stop & Cleanup Behavior
- Two-phase stop: SIGINT first (graceful, lets agent commit WIP), wait N seconds, then SIGTERM if still running
- Force-kill available as separate explicit action (bypasses graceful phase)
- Cleanup is configurable per session: default is clean up worktree + temp files on success, keep on failure/kill. User can override with `--keep` or `--cleanup` flags
- Orphaned session detection via heartbeat timeout — if no heartbeat or event received within N seconds, mark session as failed/orphaned
- No maximum session duration by default, but optional `--time-limit` flag per session to set one when needed

### Skill & Policy Config Format
- Support three config formats: JSON, YAML, and Markdown — auto-detect by file extension
- Markdown is the default format, following a structured frontmatter + content pattern for copy-to-deploy sharing between servers/hosts/sessions
- Config scoping hierarchy: default → host → project → task. Last wins (most specific scope overrides)
- Sanitization layer at API boundary — coordinator validates/sanitizes inputs when receiving StartSession or SendInput requests via existing BasicSanitizationService
- Full stdout/stderr stored per session as SessionEvents in the DB — user can replay complete session history

### Claude's Discretion
- Git worktree creation/cleanup implementation details on remote hosts
- Heartbeat interval and timeout defaults
- SIGINT-to-SIGTERM wait duration
- Markdown config parsing implementation (frontmatter extraction)
- Trust tier definitions and default policy rules
- Sanitization rule specifics and injection patterns to check

</decisions>

<specifics>
## Specific Ideas

- Config files must be portable — copyable between servers, hosts, and sessions (carried forward from Phase 1)
- Markdown-based config should use a structured format that agents can already read (agents commonly parse .md files)
- The permission merge pattern from Phase 1 (session overrides win for tool lists, OR logic for SkipPermissionPrompts) informs the approval config merge approach
- Approval flow should feel responsive — SSE event arrives quickly, user responds in their client, agent unblocks without noticeable delay

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- **IAgentAdapter + ClaudeCodeAdapter**: Agent lifecycle abstraction already built — StartAsync returns AgentProcess with stdin/stdout/stderr streams
- **AgentAdapterRegistry**: Type-based adapter resolution already working
- **DurableEventService**: Persists events to DB and broadcasts to SSE subscribers — natural fit for approval events and session monitoring
- **SseSubscriptionManager**: Manages SSE subscriber lifecycle with concurrent cleanup
- **HostCommandProtocol**: Single-line JSON over SSH protocol for host daemon commands (start-session, stop-session, report-status, ping)
- **BasicSanitizationService**: Registered in DI, ready for input validation
- **SimplePlacementEngine**: Host selection logic already exists
- **AgentHubDbContext**: Sessions, Events, Hosts tables ready for session lifecycle data

### Established Patterns
- DI registration in Program.cs with concrete implementations per interface
- Dual DbContext registration (Pool + Factory) for scoped DI and singleton services
- Singleton DurableEventService with IDbContextFactory for scoped DB access
- Enums stored as strings in SQLite for readability
- Host daemon protocol: single-line JSON for SSH stdin/stdout compatibility
- ClaudeCodeAdapter: defensive JSON parsing (attempt parse, fall back to plain StdOut)

### Integration Points
- Program.cs: ISessionCoordinator.StartSessionAsync already wired but delegates to stub InMemoryBackend — needs real SSH execution
- SshBackend registered as ISessionBackend but is a stub — needs full implementation using HostCommandProtocol
- HostSeedingService seeds hosts from hosts.json → DB on startup
- /api/sessions POST endpoint already creates sessions but doesn't execute on remote hosts
- /api/sessions/{id}/events SSE endpoint ready for session event streaming

</code_context>

<deferred>
## Deferred Ideas

- SpacetimeDB as EF Core provider alternative — evaluate when needed (from Phase 1)
- Database-backed config persistence (vs file-based) — future iteration (from Phase 1)
- MCP as primary agent control protocol — evaluate after SSH execution proves the pattern (from Phase 1)
- AI-generated session summaries — store full output first, add summarization later
- Multi-agent coordination on shared codebases — v2 requirement (SESS-07)
- Interactive bidirectional sessions — v2 requirement (SESS-06)

</deferred>

---

*Phase: 02-session-orchestration-and-agent-execution*
*Context gathered: 2026-03-08*
