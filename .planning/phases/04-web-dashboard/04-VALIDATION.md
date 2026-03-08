---
phase: 4
slug: web-dashboard
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-08
---

# Phase 4 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xunit 2.9.x + Microsoft.NET.Test.Sdk 17.x |
| **Config file** | tests/AgentHub.Tests/AgentHub.Tests.csproj |
| **Quick run command** | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~Web OR FullyQualifiedName~Dashboard OR FullyQualifiedName~SseStream" -x` |
| **Full suite command** | `dotnet test tests/AgentHub.Tests` |
| **Estimated runtime** | ~15 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~Web OR FullyQualifiedName~Dashboard OR FullyQualifiedName~SseStream" -x`
- **After every plan wave:** Run `dotnet test tests/AgentHub.Tests`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 15 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 04-01-01 | 01 | 0 | WEB-01, WEB-02 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~DashboardApiClientTests" -x` | ❌ W0 | ⬜ pending |
| 04-01-02 | 01 | 0 | WEB-02 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SseStreamServiceTests" -x` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `tests/AgentHub.Tests/DashboardApiClientTests.cs` — stubs for WEB-01 (API client methods for sessions, hosts)
- [ ] `tests/AgentHub.Tests/SseStreamServiceTests.cs` — stubs for WEB-02 (SSE consumption + Channel bridge)
- [ ] Add `ProjectReference` to `AgentHub.Web` in test project csproj

*Note: Blazor component rendering tests (bUnit) are out of scope for v1. Focus testing on the service/API client layer where the business logic lives.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Fleet overview page renders host list with session status | WEB-01 | Blazor UI rendering; bUnit out of scope v1 | Navigate to dashboard, verify hosts display with session counts |
| Real-time SSE output streams inline in session detail | WEB-02 | Requires running service + active session | Start a session, verify output appears in real-time in browser |
| Dashboard updates live without manual refresh | WEB-01 | End-to-end SSE integration | Watch dashboard while session state changes, verify auto-update |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 15s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
