# Project Retrospective

*A living document updated after each milestone. Lessons feed forward into future planning.*

## Milestone: v1.0 — AgentSafeEnv

**Shipped:** 2026-03-09
**Phases:** 6 | **Plans:** 19 | **Timeline:** 2 days

### What Was Built
- Coordinator REST API with EF Core SQLite persistence and SSE event streaming
- SSH execution backend with session lifecycle (start/stop/force-kill), approval gating, trust tiers
- CLI client with live watch, inline approvals, notifications, session input
- Blazor Server dashboard with MudBlazor fleet overview and live terminal output
- History API with typed DTOs, pagination, kind filtering — aligned across all clients
- Host metric polling via SSH with incremental SSE delivery to fleet overview

### What Worked
- Coarse phase granularity (4 core + 2 gap closure) kept planning overhead low
- Gap closure phases (5-6) caught integration issues early via audit
- Average 7min per plan — small focused plans kept context tight
- Parallel-capable phases (CLI + Web both depend on Phase 2) enabled flexibility
- DurableEventService pattern (singleton + IDbContextFactory) solved SSE + persistence cleanly

### What Was Inefficient
- Config pipeline (ConfigLoader, ConfigScopeMerger, ConfigResolutionService) built but never used — skill/policy worked via direct registries instead. Removed as tech debt
- ClaudeCodeAdapter.StartAsync() dead code — SshBackend bypasses adapter pattern entirely. Adapter abstraction exists for type metadata only
- Some plan checkboxes in ROADMAP.md not updated to [x] despite completion — caused confusion in audit
- MAUI project in scaffold has persistent build errors (XAML parser) — never addressed, deferred to v2

### Patterns Established
- Singleton service + IDbContextFactory for services needing both persistence and singleton lifetime
- Channel\<T\> bridge pattern for SseStreamReader to avoid yield-in-try-catch C# limitation
- Two-phase stop (graceful SIGTERM → force-kill after grace period) for SSH sessions
- In-place record patching with `with` expressions for SSE state updates
- Pipe-delimited metric output for OS-agnostic SSH metric parsing
- Extern alias for resolving Program type ambiguity in multi-project test setups

### Key Lessons
1. Build the execution path first, validate abstractions after — adapter pattern was architecturally clean but SshBackend went direct, leaving dead code
2. Gap closure phases are valuable — audit-driven phases 5-6 caught real integration issues (history shape, input wiring, metrics)
3. SQLite DateTimeOffset ORDER BY doesn't work — in-memory sorting needed; will need Postgres at scale
4. Aspire workload deprecated in .NET 10 — SDK 9.2.1 required forced migration early

### Cost Observations
- Model: Claude (Opus) for all planning and execution
- Sessions: ~20 context windows across 2 days
- Notable: 19 plans in ~2.2 hours total execution — average 7min/plan

---

## Cross-Milestone Trends

### Process Evolution

| Milestone | Timeline | Phases | Plans | Key Change |
|-----------|----------|--------|-------|------------|
| v1.0 | 2 days | 6 | 19 | Initial build + gap closure phases |

### Cumulative Quality

| Milestone | Tests | Audit Score | Tech Debt Items |
|-----------|-------|-------------|-----------------|
| v1.0 | 221 | 20/20 reqs | 0 (cleaned) |

### Top Lessons (Verified Across Milestones)

1. Audit before closing — gap closure phases catch real issues
2. Small focused plans (2-4 tasks) keep execution fast and context tight
