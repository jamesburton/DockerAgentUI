---
phase: 02
slug: session-orchestration-and-agent-execution
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-08
---

# Phase 02 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xunit 2.9.x with Microsoft.NET.Test.Sdk 17.x |
| **Config file** | tests/AgentHub.Tests/AgentHub.Tests.csproj |
| **Quick run command** | `dotnet test tests/AgentHub.Tests --filter "Category!=Integration" -x` |
| **Full suite command** | `dotnet test tests/AgentHub.Tests` |
| **Estimated runtime** | ~15 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test tests/AgentHub.Tests --filter "Category!=Integration" --no-build -x`
- **After every plan wave:** Run `dotnet test tests/AgentHub.Tests`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 15 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 02-01-01 | 01 | 1 | SESS-01 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SshBackendTests.StartSession"` | ❌ W0 | ⬜ pending |
| 02-01-02 | 01 | 1 | SESS-02 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SessionCoordinatorTests.ListSessions"` | ❌ W0 | ⬜ pending |
| 02-01-03 | 01 | 1 | SESS-03 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SshBackendTests.StopSession"` | ❌ W0 | ⬜ pending |
| 02-01-04 | 01 | 1 | SESS-04 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SshBackendTests.FireAndForget"` | ❌ W0 | ⬜ pending |
| 02-02-01 | 02 | 1 | SESS-05 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SessionHistoryTests"` | ❌ W0 | ⬜ pending |
| 02-02-02 | 02 | 1 | AGENT-02 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~ConfigLoaderTests"` | ❌ W0 | ⬜ pending |
| 02-02-03 | 02 | 1 | AGENT-03 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SanitizationTests"` | ❌ W0 | ⬜ pending |
| 02-03-01 | 03 | 2 | AGENT-04 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~ApprovalServiceTests"` | ❌ W0 | ⬜ pending |
| 02-03-02 | 03 | 2 | AGENT-05 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~PermissionFlagTests"` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `tests/AgentHub.Tests/SshBackendTests.cs` — stubs for SESS-01, SESS-03, SESS-04 (mock SSH.NET client)
- [ ] `tests/AgentHub.Tests/SessionCoordinatorTests.cs` — stubs for SESS-02 (DB-backed listing)
- [ ] `tests/AgentHub.Tests/SessionHistoryTests.cs` — stubs for SESS-05 (query/pagination)
- [ ] `tests/AgentHub.Tests/ConfigLoaderTests.cs` — stubs for AGENT-02 (JSON/YAML/MD parsing + scope merge)
- [ ] `tests/AgentHub.Tests/SanitizationTests.cs` — stubs for AGENT-03 (extended sanitization rules)
- [ ] `tests/AgentHub.Tests/ApprovalServiceTests.cs` — stubs for AGENT-04 (approval flow + timeout)
- [ ] `tests/AgentHub.Tests/PermissionFlagTests.cs` — stubs for AGENT-05 (skip-permissions wiring)
- [ ] `tests/AgentHub.Tests/Helpers/MockSshClient.cs` — shared mock for SSH.NET

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Real SSH session to remote host | SESS-01 | Requires actual remote host with agent installed | 1. Register test host in hosts.json 2. POST /api/sessions with real host target 3. Verify agent process starts on remote host |
| SSE event delivery to browser | MON-01 | Requires real browser SSE connection | 1. Open /api/events in browser 2. Launch session 3. Verify events stream in real-time |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 15s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
