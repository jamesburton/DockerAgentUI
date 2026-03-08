---
phase: 01-foundation-and-event-infrastructure
plan: 03
subsystem: agent-adapters
tags: [cliwrap, agent-adapter, host-daemon, json-protocol, claude-code, xunit]

requires:
  - phase: 01-foundation-and-event-infrastructure/01
    provides: EF Core DbContext, AgentTypes.cs with AgentType enum and AgentPermissions

provides:
  - IAgentAdapter interface separating agent concerns from backend concerns
  - ClaudeCodeAdapter with CliWrap CLI invocation and defensive JSON parsing
  - AgentAdapterRegistry for DI-based adapter resolution by type string
  - AgentAdapterModels (AgentStartRequest, AgentProcess, AgentOutputLine, AgentAdapterConfig)
  - PermissionMerger for session-override-wins permission merging
  - Host daemon JSON-over-SSH command protocol (start-session, stop-session, report-status, ping)
  - HostCommand/HostCommandResponse models with single-line JSON serialization
  - Agent config file pattern (config/agents/claude-code.json)
  - GET /api/agents endpoint returning supported agent types

affects: [02-ssh-backend, 02-host-daemon, phase-2-execution]

tech-stack:
  added: [CliWrap 3.10.0]
  patterns: [adapter-pattern for agent abstraction, DI-injected adapter registry, JSON-over-SSH protocol, defensive JSON parsing for CLI output, permission-merge strategy]

key-files:
  created:
    - src/AgentHub.Orchestration/Agents/IAgentAdapter.cs
    - src/AgentHub.Orchestration/Agents/AgentAdapterModels.cs
    - src/AgentHub.Orchestration/Agents/ClaudeCodeAdapter.cs
    - src/AgentHub.Orchestration/Agents/AgentAdapterRegistry.cs
    - src/AgentHub.Orchestration/HostDaemon/HostCommandProtocol.cs
    - src/AgentHub.Orchestration/HostDaemon/HostDaemonModels.cs
    - config/agents/claude-code.json
    - tests/AgentHub.Tests/AgentAdapterTests.cs
    - tests/AgentHub.Tests/HostDaemonProtocolTests.cs
  modified:
    - src/AgentHub.Orchestration/AgentHub.Orchestration.csproj
    - src/AgentHub.Service/Program.cs

key-decisions:
  - "Separate AgentAdapterConfig (JSON-serializable with string agentType) from Contracts AgentDefinition (enum-based) for flexible adapter config"
  - "Permission merge strategy: session overrides win for tool lists, OR logic for SkipPermissionPrompts"
  - "Host daemon protocol uses single-line JSON for SSH stdin/stdout compatibility"
  - "ClaudeCodeAdapter uses defensive JSON parsing: attempts JSON parse, falls back to plain StdOut on failure"

patterns-established:
  - "Agent adapter pattern: IAgentAdapter.BuildCommandArgs for testable CLI argument construction, StartAsync for process lifecycle"
  - "Adapter registry: DI-injected IEnumerable<IAgentAdapter> with case-insensitive type lookup"
  - "Agent config files: config/agents/{type}.json with AgentAdapterConfig schema including default permissions"
  - "Host daemon protocol: single-line JSON commands with version field, factory methods for construction, static serialization helpers"

requirements-completed: [AGENT-01, INFRA-02]

duration: 6min
completed: 2026-03-08
---

# Phase 1 Plan 03: Agent Adapter Abstraction & Host Daemon Protocol Summary

**IAgentAdapter with ClaudeCodeAdapter using CliWrap, adapter registry with DI, host daemon JSON-over-SSH command protocol, and 25 passing tests**

## Performance

- **Duration:** 6 min
- **Started:** 2026-03-08T10:47:06Z
- **Completed:** 2026-03-08T10:53:55Z
- **Tasks:** 2
- **Files modified:** 11

## Accomplishments
- IAgentAdapter interface cleanly separates agent CLI concerns from session backend concerns, making it trivial to add new agent types
- ClaudeCodeAdapter builds correct CLI argument lists for all permission combinations and uses defensive JSON parsing for stream-json output
- Host daemon command protocol defines the wire format for coordinator-to-host communication over SSH (start-session, stop-session, report-status, ping)
- 25 unit tests covering adapter argument building, registry resolution, config deserialization, permission merging, and protocol serialization

## Task Commits

Each task was committed atomically:

1. **Task 1: IAgentAdapter, models, registry, ClaudeCodeAdapter, config, tests** - `6be2fee` (feat)
2. **Task 2: Host daemon protocol, DI wiring, /api/agents endpoint, tests** - `832e429` (feat)

## Files Created/Modified
- `src/AgentHub.Orchestration/Agents/IAgentAdapter.cs` - Agent adapter interface with AgentType, StartAsync, BuildCommandArgs
- `src/AgentHub.Orchestration/Agents/AgentAdapterModels.cs` - AgentStartRequest, AgentProcess, AgentOutputLine, AgentAdapterConfig, PermissionMerger
- `src/AgentHub.Orchestration/Agents/ClaudeCodeAdapter.cs` - Claude Code CLI adapter with CliWrap invocation and defensive JSON parsing
- `src/AgentHub.Orchestration/Agents/AgentAdapterRegistry.cs` - Registry resolving adapters by type string via DI
- `src/AgentHub.Orchestration/HostDaemon/HostDaemonModels.cs` - HostCommand, HostCommandResponse, StartSessionPayload, HostStatusReport
- `src/AgentHub.Orchestration/HostDaemon/HostCommandProtocol.cs` - Static factory and serialization methods for host daemon commands
- `config/agents/claude-code.json` - Default Claude Code agent configuration with permission defaults
- `src/AgentHub.Orchestration/AgentHub.Orchestration.csproj` - Added CliWrap 3.10.0 package reference
- `src/AgentHub.Service/Program.cs` - DI registration for IAgentAdapter, AgentAdapterRegistry; GET /api/agents endpoint
- `tests/AgentHub.Tests/AgentAdapterTests.cs` - 15 tests for adapter, registry, config, permissions
- `tests/AgentHub.Tests/HostDaemonProtocolTests.cs` - 10 tests for protocol serialization and round-trip

## Decisions Made
- Created separate AgentAdapterConfig record (JSON-friendly with string agentType) rather than reusing the Contracts AgentDefinition (which uses the AgentType enum) -- allows flexible config file format while keeping strongly-typed enums in the domain
- Permission merge strategy: session overrides win for tool lists (AllowedTools, DisallowedTools), OR logic for SkipPermissionPrompts (any true wins)
- Host daemon protocol uses single-line JSON to ensure compatibility with SSH stdin/stdout piping
- ClaudeCodeAdapter defensively parses each stdout line as JSON, falling back to plain StdOut on parse failure -- handles CLI format instability

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered
- Pre-existing SSE streaming test failures (3 tests in SseStreamingTests) from Plan 01-02 -- not caused by this plan's changes, not addressed
- Pre-existing Maui project build errors -- excluded from verification as noted in Plan 01-01

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Agent adapter pattern established and ready for Phase 2 SSH backend to use IAgentAdapter for remote agent invocation
- Host daemon protocol ready for Phase 2 host daemon implementation (JSON-over-SSH execution)
- AgentAdapterRegistry can be extended by registering additional IAgentAdapter implementations in DI
- config/agents/ directory pattern established for per-agent-type configuration files

---
*Phase: 01-foundation-and-event-infrastructure*
*Completed: 2026-03-08*
