---
phase: 1
slug: foundation-and-event-infrastructure
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-08
---

# Phase 1 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit + Microsoft.AspNetCore.Mvc.Testing |
| **Config file** | none — Wave 0 installs |
| **Quick run command** | `dotnet test tests/AgentHub.Tests/ --filter "Category!=Integration"` |
| **Full suite command** | `dotnet test tests/AgentHub.Tests/` |
| **Estimated runtime** | ~15 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test tests/AgentHub.Tests/ --filter "Category!=Integration"`
- **After every plan wave:** Run `dotnet test tests/AgentHub.Tests/`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 15 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 01-01-01 | 01 | 0 | ALL | setup | `dotnet test tests/AgentHub.Tests/` | ❌ W0 | ⬜ pending |
| 01-02-01 | 02 | 1 | INFRA-01 | integration | `dotnet test tests/AgentHub.Tests/ --filter "FullyQualifiedName~ApiEndpoints"` | ❌ W0 | ⬜ pending |
| 01-02-02 | 02 | 1 | INFRA-03 | integration | `dotnet test tests/AgentHub.Tests/ --filter "FullyQualifiedName~Persistence"` | ❌ W0 | ⬜ pending |
| 01-03-01 | 03 | 1 | MON-01 | integration | `dotnet test tests/AgentHub.Tests/ --filter "FullyQualifiedName~SseStreaming"` | ❌ W0 | ⬜ pending |
| 01-04-01 | 04 | 2 | AGENT-01 | unit | `dotnet test tests/AgentHub.Tests/ --filter "FullyQualifiedName~AgentAdapter"` | ❌ W0 | ⬜ pending |
| 01-04-02 | 04 | 2 | INFRA-02 | unit | `dotnet test tests/AgentHub.Tests/ --filter "FullyQualifiedName~HostDaemon"` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `tests/AgentHub.Tests/AgentHub.Tests.csproj` — new test project with xUnit, Microsoft.AspNetCore.Mvc.Testing, Microsoft.EntityFrameworkCore.InMemory
- [ ] `tests/AgentHub.Tests/ApiEndpointTests.cs` — WebApplicationFactory-based integration test stubs for INFRA-01
- [ ] `tests/AgentHub.Tests/PersistenceTests.cs` — EF Core in-memory provider test stubs for INFRA-03
- [ ] `tests/AgentHub.Tests/SseStreamingTests.cs` — SSE endpoint test stubs for MON-01
- [ ] `tests/AgentHub.Tests/AgentAdapterTests.cs` — Claude Code adapter unit test stubs for AGENT-01
- [ ] `tests/AgentHub.Tests/HostDaemonProtocolTests.cs` — protocol definition test stubs for INFRA-02
- [ ] Solution file updated to include test project

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Host daemon SSH connectivity | INFRA-02 | Requires real SSH target | Verify stub protocol structure compiles and serializes correctly |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 15s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
