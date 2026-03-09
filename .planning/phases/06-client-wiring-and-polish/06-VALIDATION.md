---
phase: 6
slug: client-wiring-and-polish
status: draft
nyquist_compliant: false
wave_0_complete: false
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
| 06-01-01 | 01 | 1 | AGENT-03 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~ApiClientTests.SendInputAsync" --no-build -v q` | ❌ W0 | ⬜ pending |
| 06-01-02 | 01 | 1 | AGENT-03 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~DashboardApiClientTests.SendInputAsync" --no-build -v q` | ❌ W0 | ⬜ pending |
| 06-01-03 | 01 | 1 | AGENT-04 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SessionCoordinatorTests" --no-build -v q` | ✅ | ⬜ pending |
| 06-02-01 | 02 | 2 | MON-02 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~HostMetricTests" --no-build -v q` | ❌ W0 | ⬜ pending |
| 06-02-02 | 02 | 2 | MON-02 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~HostMetricTests.EmitsHostMetricsEvent" --no-build -v q` | ❌ W0 | ⬜ pending |
| 06-03-01 | 03 | 2 | — | integration | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~FleetOverviewTests" --no-build -v q` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `tests/AgentHub.Tests/ApiClientTests.cs` — add SendInputAsync test methods (file exists, tests don't)
- [ ] `tests/AgentHub.Tests/DashboardApiClientTests.cs` — add SendInputAsync test methods (file exists, tests don't)
- [ ] `tests/AgentHub.Tests/HostMetricTests.cs` — new file for metric polling and SSE emission tests
- [ ] `tests/AgentHub.Tests/FleetOverviewTests.cs` — new file for incremental patching tests

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

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 15s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
