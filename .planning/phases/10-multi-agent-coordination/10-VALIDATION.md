---
phase: 10
slug: multi-agent-coordination
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-22
---

# Phase 10 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xunit 2.9.x on .NET 10 |
| **Config file** | tests/AgentHub.Tests/AgentHub.Tests.csproj |
| **Quick run command** | `dotnet test tests/AgentHub.Tests --filter "Category=Phase10" -x` |
| **Full suite command** | `dotnet test tests/AgentHub.Tests` |
| **Estimated runtime** | ~15 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test tests/AgentHub.Tests --filter "Category=Phase10" -x`
- **After every plan wave:** Run `dotnet test tests/AgentHub.Tests`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 15 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 10-01-01 | 01 | 1 | COORD-04 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~PlacementScoring" -x` | ❌ W0 | ⬜ pending |
| 10-01-02 | 01 | 1 | COORD-05 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~CascadeLimit" -x` | ❌ W0 | ⬜ pending |
| 10-01-03 | 01 | 1 | COORD-01 | integration | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SpawnSession" -x` | ❌ W0 | ⬜ pending |
| 10-02-01 | 02 | 1 | COORD-02 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~ParentChild" -x` | ❌ W0 | ⬜ pending |
| 10-02-02 | 02 | 1 | COORD-03 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~ChildEventForwarding" -x` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `tests/AgentHub.Tests/PlacementScoringTests.cs` — stubs for COORD-04 weighted scoring
- [ ] `tests/AgentHub.Tests/CascadeLimitTests.cs` — stubs for COORD-05 depth/count validation
- [ ] `tests/AgentHub.Tests/ChildEventForwardingTests.cs` — stubs for COORD-03 parent stream routing
- [ ] `tests/AgentHub.Tests/SpawnSessionTests.cs` — stubs for COORD-01, COORD-02 spawn + persistence

*Existing test infrastructure covers framework setup.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Tree view renders parent-child hierarchy in Web UI | COORD-02 | Visual rendering in MudBlazor | Launch parent+child sessions, verify tree indentation in session list |
| CLI tree displays indented children in `ah session list` | COORD-02 | Spectre.Console terminal rendering | Run `ah session list` with parent-child sessions, verify tree connectors |
| SSH stdout intercept spawns child session | COORD-01 | Requires live SSH + agent process | Run agent that outputs spawn marker, verify child session created |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 15s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
