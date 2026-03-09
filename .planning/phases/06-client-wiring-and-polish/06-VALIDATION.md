---
phase: 6
slug: client-wiring-and-polish
status: complete
nyquist_compliant: true
wave_0_complete: true
created: 2026-03-09
---

# Phase 6 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xunit 2.9.x + Microsoft.NET.Test.Sdk 17.x |
| **Config file** | tests/AgentHub.Tests/AgentHub.Tests.csproj |
| **Quick run command** | `dotnet test tests/AgentHub.Tests --no-build -v q` |
| **Full suite command** | `dotnet test tests/AgentHub.Tests -v q` |
| **Estimated runtime** | ~15 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test tests/AgentHub.Tests --no-build -v q`
- **After every plan wave:** Run `dotnet test tests/AgentHub.Tests -v q`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 15 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 06-01-01 | 01 | 1 | AGENT-03 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~ApiClientTests.SendInputAsync" --no-build -v q` | ✅ | ✅ green (4 tests) |
| 06-01-02 | 01 | 1 | AGENT-03 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~DashboardApiClientTests.SendInputAsync" --no-build -v q` | ✅ | ✅ green (2 tests) |
| 06-01-03 | 01 | 1 | AGENT-04 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SessionCoordinatorTests" --no-build -v q` | ✅ | ✅ green (7 tests) |
| 06-02-01 | 02 | 2 | MON-02 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~HostMetricTests" --no-build -v q` | ✅ | ✅ green (13 tests) |
| 06-02-02 | 02 | 2 | MON-02 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~HostMetricTests.EmitsHostMetricsEvent" --no-build -v q` | ✅ | ✅ green (included above) |
| 06-03-01 | 03 | 2 | MON-02 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~FleetOverviewTests" --no-build -v q` | ✅ | ✅ green (7 tests) |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [x] `tests/AgentHub.Tests/ApiClientTests.cs` — SendInputAsync tests added during plan execution (4 tests)
- [x] `tests/AgentHub.Tests/DashboardApiClientTests.cs` — SendInputAsync tests added during plan execution (2 tests)
- [x] `tests/AgentHub.Tests/HostMetricTests.cs` — metric parsing, command, DTO tests added during plan execution (13 tests)
- [x] `tests/AgentHub.Tests/FleetOverviewTests.cs` — incremental patching tests added by Nyquist auditor (7 tests)

*If none: "Existing infrastructure covers all phase requirements."*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| CLI watch 'i' hotkey enters input mode | AGENT-03 | Console.KeyAvailable interaction with Spectre.Console Live | Run `ah session watch <id>`, press 'i', type text, press Enter, verify sent |
| Web input bar appears only when Running | AGENT-03 | Visual UI behavior | Open SessionDetail for a Running session, verify input bar visible; open for Completed, verify hidden |
| Host metrics display in sidebar | MON-02 | Visual rendering + SSH integration | Check HostSidebar shows CPU/memory bars with real values after 30s poll |
| Stale metric indicator (dimmed, age) | MON-02 | Visual styling | Stop metrics polling, wait >60s, verify values dim and show age |

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or Wave 0 dependencies
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers all MISSING references
- [x] No watch-mode flags
- [x] Feedback latency < 15s
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** complete

---

## Validation Audit 2026-03-09
| Metric | Count |
|--------|-------|
| Gaps found | 1 |
| Resolved | 1 |
| Escalated | 0 |
