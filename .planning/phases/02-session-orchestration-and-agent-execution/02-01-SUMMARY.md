---
phase: 02-session-orchestration-and-agent-execution
plan: 01
subsystem: orchestration
tags: [ef-core, yaml, config, trust-tiers, approval, protocol]

requires:
  - phase: 01-foundation-and-event-infrastructure
    provides: "SessionEntity, SessionEventKind, HostCommandProtocol, AgentHubDbContext, SkillPolicyDocument"
provides:
  - "Extended SessionEventKind with 6 Phase 2 values (ApprovalRequest through CleanupCompleted)"
  - "SessionEntity with 7 Phase 2 properties (CompletedUtc, ExitCode, CleanupState, etc.)"
  - "ApprovalEntity for approval request persistence with DbSet"
  - "ForceKill and ApprovalResponse protocol commands"
  - "TrustTier enum and ScopedPolicyConfig model"
  - "Multi-format ConfigLoader (JSON/YAML/Markdown)"
  - "ConfigScopeMerger with 4-level hierarchy merge"
  - "ConfigDiscovery for scope-aware config file search"
  - "StartSessionRequest with Prompt and IsFireAndForget fields"
affects: [02-02, 02-03, 02-04]

tech-stack:
  added: [YamlDotNet 16.3.0, Markdig 0.38.0, SSH.NET 2025.1.0]
  patterns: [multi-format-config-loading, scope-hierarchy-merge, trust-tier-gating, generated-regex-frontmatter]

key-files:
  created:
    - src/AgentHub.Orchestration/Config/ConfigLoader.cs
    - src/AgentHub.Orchestration/Config/ConfigScopeMerger.cs
    - src/AgentHub.Orchestration/Data/Entities/ApprovalEntity.cs
    - src/AgentHub.Orchestration/Migrations/20260308150833_Phase2Models.cs
    - tests/AgentHub.Tests/PermissionFlagTests.cs
    - tests/AgentHub.Tests/ConfigLoaderTests.cs
  modified:
    - src/AgentHub.Contracts/Models.cs
    - src/AgentHub.Orchestration/Config/PolicyModels.cs
    - src/AgentHub.Orchestration/Data/AgentHubDbContext.cs
    - src/AgentHub.Orchestration/Data/Entities/SessionEntity.cs
    - src/AgentHub.Orchestration/HostDaemon/HostCommandProtocol.cs
    - src/AgentHub.Orchestration/HostDaemon/HostDaemonModels.cs
    - src/AgentHub.Orchestration/AgentHub.Orchestration.csproj

key-decisions:
  - "Used GeneratedRegex for Markdown frontmatter extraction (compile-time regex for performance)"
  - "ScopedPolicyConfig uses mutable class (not record) for flexible YAML/JSON deserialization with property setters"
  - "ConfigScopeMerger ElevatedSkills use union merge (any scope can elevate) vs last-wins for enable/disable"

patterns-established:
  - "ConfigLoader.Load<T>(path): Multi-format config loading by extension detection"
  - "ConfigScopeMerger.Merge(): 4-level scope hierarchy with explicit merge semantics per field type"
  - "ConfigDiscovery.FindConfigFiles(): Convention-based scope directory discovery"
  - "DefaultTrustTiers: Static default trust tier definitions for action-level gating"

requirements-completed: [AGENT-02, AGENT-05]

duration: 7min
completed: 2026-03-08
---

# Phase 2 Plan 1: Data Models, Config System, and Protocol Extensions Summary

**Extended data models with Phase 2 lifecycle fields, built multi-format config system (JSON/YAML/Markdown) with 4-level scoping hierarchy, and wired permission-skip flags through protocol**

## Performance

- **Duration:** 7 min
- **Started:** 2026-03-08T15:05:35Z
- **Completed:** 2026-03-08T15:12:10Z
- **Tasks:** 2
- **Files modified:** 17

## Accomplishments
- Extended SessionEventKind with 6 new values and SessionEntity with 7 Phase 2 properties
- Created ApprovalEntity with full persistence and DbContext integration
- Built ConfigLoader supporting JSON, YAML, and Markdown (with frontmatter extraction and body preservation)
- Implemented ConfigScopeMerger with correct merge semantics: last-wins for scalars, union for deny-lists, OR for skip-permissions
- Added ForceKill and ApprovalResponse protocol commands with factory methods
- Defined TrustTier enum and ScopedPolicyConfig model with default trust tier definitions
- Installed YamlDotNet 16.3.0, Markdig 0.38.0, SSH.NET 2025.1.0
- All 78 tests pass (38 new + 40 existing)

## Task Commits

Each task was committed atomically:

1. **Task 1: Extend data models, enum values, protocol commands, and policy models** - `9110bc5` (feat)
2. **Task 2: Multi-format ConfigLoader with scoping hierarchy merge** - `0a16e46` (feat)

_TDD approach: tests written first, then implementation to pass them._

## Files Created/Modified
- `src/AgentHub.Contracts/Models.cs` - Extended SessionEventKind (6 new values), StartSessionRequest (Prompt, IsFireAndForget)
- `src/AgentHub.Orchestration/Data/Entities/SessionEntity.cs` - 7 new Phase 2 properties
- `src/AgentHub.Orchestration/Data/Entities/ApprovalEntity.cs` - Approval request persistence entity
- `src/AgentHub.Orchestration/Data/AgentHubDbContext.cs` - Added Approvals DbSet with FK/index configuration
- `src/AgentHub.Orchestration/HostDaemon/HostDaemonModels.cs` - ForceKill, ApprovalResponse constants; ForceKillPayload, ApprovalResponsePayload records
- `src/AgentHub.Orchestration/HostDaemon/HostCommandProtocol.cs` - CreateForceKill and CreateApprovalResponse factory methods
- `src/AgentHub.Orchestration/Config/PolicyModels.cs` - TrustTier enum, ScopedPolicyConfig, DefaultTrustTiers
- `src/AgentHub.Orchestration/Config/ConfigLoader.cs` - Multi-format config loader (JSON/YAML/MD)
- `src/AgentHub.Orchestration/Config/ConfigScopeMerger.cs` - Scoping hierarchy merge logic + ConfigDiscovery
- `src/AgentHub.Orchestration/AgentHub.Orchestration.csproj` - Added YamlDotNet, Markdig, SSH.NET packages
- `src/AgentHub.Orchestration/Migrations/20260308150833_Phase2Models.cs` - EF Core migration for schema changes
- `tests/AgentHub.Tests/PermissionFlagTests.cs` - 19 tests for data models, protocol, trust tiers
- `tests/AgentHub.Tests/ConfigLoaderTests.cs` - 19 tests for config loading, merging, discovery

## Decisions Made
- Used GeneratedRegex for Markdown frontmatter extraction (compile-time regex for better performance)
- ScopedPolicyConfig is a mutable class rather than a record to support flexible YAML/JSON deserialization via property setters
- ElevatedSkills use union merge semantics (any scope can elevate a skill) while EnabledSkills/DisabledSkills use last-wins (session overrides)

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness
- All data models, protocol extensions, and config system ready for Plans 02-02 through 02-04
- ApprovalEntity and TrustTier ready for approval flow implementation (Plan 02-03)
- ConfigLoader and ConfigScopeMerger ready for SkillPolicyService rewrite (used by Plan 02-02)
- SSH.NET installed and ready for SshBackend implementation (Plan 02-02)
- ForceKill protocol command ready for two-phase stop implementation (Plan 02-02)

---
*Phase: 02-session-orchestration-and-agent-execution*
*Completed: 2026-03-08*
