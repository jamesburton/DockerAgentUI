---
phase: 7
slug: infrastructure-host-inventory
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-09
---

# Phase 7 — Validation Strategy

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
| 07-01-01 | 01 | 1 | INFRA-01 | integration | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~InventoryMigrationTests" -v q` | ❌ W0 | ⬜ pending |
| 07-01-02 | 01 | 1 | INFRA-02 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~HostMetricCacheTests" -v q` | ❌ W0 | ⬜ pending |
| 07-02-01 | 02 | 1 | INVT-01 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~HostInventoryTests" -v q` | ❌ W0 | ⬜ pending |
| 07-02-02 | 02 | 1 | INVT-02 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~HostInventoryTests" -v q` | ❌ W0 | ⬜ pending |
| 07-02-03 | 02 | 1 | INVT-03 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~HostInventoryTests" -v q` | ❌ W0 | ⬜ pending |
| 07-02-04 | 02 | 1 | INVT-04 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~HostInventoryTests" -v q` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `tests/AgentHub.Tests/InventoryMigrationTests.cs` — stubs for INFRA-01
- [ ] `tests/AgentHub.Tests/HostMetricCacheTests.cs` — stubs for INFRA-02
- [ ] `tests/AgentHub.Tests/HostInventoryTests.cs` — stubs for INVT-01, INVT-02, INVT-03, INVT-04

*Existing xunit framework covers all dependencies.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Dashboard displays agent CLI versions | INVT-02 | Visual UI verification | Navigate to host details, verify agent versions shown |
| Force refresh button triggers inventory scan | INVT-04 | UI interaction + real SSH | Click refresh button, verify inventory updates |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 15s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
