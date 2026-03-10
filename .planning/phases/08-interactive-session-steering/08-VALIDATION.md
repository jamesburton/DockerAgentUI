---
phase: 8
slug: interactive-session-steering
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-10
---

# Phase 8 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.x |
| **Config file** | tests/AgentHub.Tests/AgentHub.Tests.csproj |
| **Quick run command** | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~Steering" -x` |
| **Full suite command** | `dotnet test tests/AgentHub.Tests` |
| **Estimated runtime** | ~15 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~Steering" -x`
- **After every plan wave:** Run `dotnet test tests/AgentHub.Tests`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 15 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 08-01-01 | 01 | 0 | INTER-01 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SteeringInput" --no-build` | ❌ W0 | ⬜ pending |
| 08-01-02 | 01 | 0 | INTER-01 | integration | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SteeringEndpoint" --no-build` | ❌ W0 | ⬜ pending |
| 08-01-03 | 01 | 0 | INTER-02 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SteeringFormat" --no-build` | ❌ W0 | ⬜ pending |
| 08-01-04 | 01 | 0 | INTER-02 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SteeringCss" --no-build` | ❌ W0 | ⬜ pending |
| 08-01-05 | 01 | 0 | INTER-03 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SteeringDelivery" --no-build` | ❌ W0 | ⬜ pending |
| 08-01-06 | 01 | 0 | INTER-03 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SteeringDelivered" --no-build` | ❌ W0 | ⬜ pending |
| 08-01-07 | 01 | 0 | INTER-03 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SteeringTimeout" --no-build` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `tests/AgentHub.Tests/SteeringTests.cs` — stubs for INTER-01, INTER-02, INTER-03 (coordinator, event emission, formatting)
- [ ] `tests/AgentHub.Tests/SteeringDeliveryTests.cs` — stubs for INTER-03 (backend delivery confirmation, timeout)

*Existing test helpers (Helpers/ directory) likely sufficient for mocking — no new shared fixtures expected.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Follow-up input visually distinct in Blazor UI | INTER-02 | CSS styling requires visual inspection | 1. Start session 2. Send follow-up 3. Verify STEER prefix and styling |
| CLI live display updates with steering indicator | INTER-02 | Spectre.Console Live context is hard to test in headless mode | 1. Run `agenthub session watch` 2. Send follow-up via another terminal 3. Verify visual distinction |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 15s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
