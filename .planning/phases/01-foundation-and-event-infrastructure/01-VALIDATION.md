---
phase: 1
slug: foundation-and-event-infrastructure
status: complete
nyquist_compliant: true
wave_0_complete: true
created: 2026-03-08
audited: 2026-03-09
---

# Phase 1 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit + Microsoft.AspNetCore.Mvc.Testing |
| **Config file** | none |
| **Quick run command** | `dotnet test tests/AgentHub.Tests/ --filter "Category!=Integration"` |
| **Full suite command** | `dotnet test tests/AgentHub.Tests/` |
| **Estimated runtime** | ~4 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test tests/AgentHub.Tests/ --filter "Category!=Integration"`
- **After every plan wave:** Run `dotnet test tests/AgentHub.Tests/`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 4 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 01-01-01 | 01 | 0 | ALL | setup | `dotnet test tests/AgentHub.Tests/` | ✅ | ✅ green |
| 01-02-01 | 02 | 1 | INFRA-01 | integration | `dotnet test tests/AgentHub.Tests/ --filter "FullyQualifiedName~ApiEndpoint"` | ✅ ApiEndpointTests.cs (3 tests) | ✅ green |
| 01-02-02 | 02 | 1 | INFRA-03 | integration | `dotnet test tests/AgentHub.Tests/ --filter "FullyQualifiedName~Persistence"` | ✅ PersistenceTests.cs (7 tests) | ✅ green |
| 01-03-01 | 03 | 1 | MON-01 | integration | `dotnet test tests/AgentHub.Tests/ --filter "FullyQualifiedName~SseStreaming|FullyQualifiedName~DurableEventService"` | ✅ SseStreamingTests.cs (5) + DurableEventServiceTests.cs (8) | ✅ green |
| 01-04-01 | 04 | 2 | AGENT-01 | unit | `dotnet test tests/AgentHub.Tests/ --filter "FullyQualifiedName~AgentAdapter"` | ✅ AgentAdapterTests.cs (15 tests) | ✅ green |
| 01-04-02 | 04 | 2 | INFRA-02 | unit | `dotnet test tests/AgentHub.Tests/ --filter "FullyQualifiedName~HostDaemon"` | ✅ HostDaemonProtocolTests.cs (10 tests) | ✅ green |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [x] `tests/AgentHub.Tests/AgentHub.Tests.csproj` — test project with xUnit, Microsoft.AspNetCore.Mvc.Testing, Microsoft.EntityFrameworkCore.InMemory
- [x] `tests/AgentHub.Tests/ApiEndpointTests.cs` — WebApplicationFactory-based integration tests for INFRA-01 (3 tests)
- [x] `tests/AgentHub.Tests/PersistenceTests.cs` — EF Core persistence tests for INFRA-03 (7 tests)
- [x] `tests/AgentHub.Tests/SseStreamingTests.cs` — SSE endpoint integration tests for MON-01 (5 tests)
- [x] `tests/AgentHub.Tests/DurableEventServiceTests.cs` — Event service unit tests for MON-01 (8 tests)
- [x] `tests/AgentHub.Tests/AgentAdapterTests.cs` — Claude Code adapter unit tests for AGENT-01 (15 tests)
- [x] `tests/AgentHub.Tests/HostDaemonProtocolTests.cs` — Protocol tests for INFRA-02 (10 tests)
- [x] Solution file includes test project

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Host daemon SSH connectivity | INFRA-02 | Requires real SSH target | Verify stub protocol structure compiles and serializes correctly |

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or Wave 0 dependencies
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers all MISSING references
- [x] No watch-mode flags
- [x] Feedback latency < 15s (actual: ~4s)
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** passed

---

## Validation Audit 2026-03-09

| Metric | Count |
|--------|-------|
| Gaps found | 0 |
| Resolved | 0 |
| Escalated | 0 |
| Total tests | 48 |
| All green | yes |

All 5 requirements (INFRA-01, INFRA-02, INFRA-03, MON-01, AGENT-01) have automated test coverage. No gaps to fill.
