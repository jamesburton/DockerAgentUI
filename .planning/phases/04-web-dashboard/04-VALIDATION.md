---
phase: 4
slug: web-dashboard
status: complete
nyquist_compliant: true
wave_0_complete: true
created: 2026-03-08
audited: 2026-03-09
---

# Phase 4 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xunit 2.9.x + Microsoft.NET.Test.Sdk 17.x |
| **Config file** | tests/AgentHub.Tests/AgentHub.Tests.csproj |
| **Quick run command** | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~DashboardApiClient|FullyQualifiedName~SseStreamService"` |
| **Full suite command** | `dotnet test tests/AgentHub.Tests` |
| **Estimated runtime** | ~1 second |

---

## Sampling Rate

- **After every task commit:** Run quick command above
- **After every plan wave:** Run `dotnet test tests/AgentHub.Tests`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 1 second

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 04-01-01 | 01 | 0 | WEB-01 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~DashboardApiClientTests"` | ✅ DashboardApiClientTests.cs (10 tests) | ✅ green |
| 04-01-02 | 01 | 0 | WEB-02 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SseStreamServiceTests"` | ✅ SseStreamServiceTests.cs (3 tests) | ✅ green |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [x] `tests/AgentHub.Tests/DashboardApiClientTests.cs` — WEB-01 API client: sessions CRUD, hosts, history, approval (10 tests)
- [x] `tests/AgentHub.Tests/SseStreamServiceTests.cs` — WEB-02 SSE consumption + Channel bridge (3 tests)
- [x] `AgentHub.Web` ProjectReference in test project csproj (with WebApp alias)

*Note: Blazor component rendering tests (bUnit) are out of scope for v1. Testing focused on service/API client layer.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Fleet overview page renders host list with session status | WEB-01 | Blazor UI rendering; bUnit out of scope v1 | Navigate to dashboard, verify hosts display with session counts |
| Real-time SSE output streams inline in session detail | WEB-02 | Requires running service + active session | Start a session, verify output appears in real-time in browser |
| Dashboard updates live without manual refresh | WEB-01 | End-to-end SSE integration | Watch dashboard while session state changes, verify auto-update |
| Dark theme toggle works | WEB-01 | Visual rendering | Toggle theme switch, verify colors change |
| Terminal output color coding | WEB-02 | Visual CSS rendering | View session detail, verify stdout/stderr/state colors |

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or Wave 0 dependencies
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers all MISSING references
- [x] No watch-mode flags
- [x] Feedback latency < 15s (actual: ~1s)
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** passed

---

## Validation Audit 2026-03-09

| Metric | Count |
|--------|-------|
| Gaps found | 0 |
| Resolved | 0 |
| Escalated | 0 |
| Total tests | 13 |
| All green | yes |

Both requirements (WEB-01, WEB-02) have automated test coverage via DashboardApiClientTests and SseStreamServiceTests. No gaps to fill.
