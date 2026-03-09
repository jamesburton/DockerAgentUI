---
phase: 02
slug: session-orchestration-and-agent-execution
status: complete
nyquist_compliant: true
wave_0_complete: true
created: 2026-03-08
audited: 2026-03-09
---

# Phase 02 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xunit 2.9.x with Microsoft.NET.Test.Sdk 17.x |
| **Config file** | tests/AgentHub.Tests/AgentHub.Tests.csproj |
| **Quick run command** | `dotnet test tests/AgentHub.Tests --filter "Category!=Integration"` |
| **Full suite command** | `dotnet test tests/AgentHub.Tests` |
| **Estimated runtime** | ~5 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test tests/AgentHub.Tests --filter "Category!=Integration" --no-build`
- **After every plan wave:** Run `dotnet test tests/AgentHub.Tests`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 5 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 02-01-01 | 01 | 1 | SESS-01 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SshBackendTests.StartAsync"` | ✅ SshBackendTests.cs (4 tests) | ✅ green |
| 02-01-02 | 01 | 1 | SESS-02 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SshBackendTests.ListAsync|FullyQualifiedName~SessionCoordinatorTests.ListSessions"` | ✅ SshBackendTests.cs + SessionCoordinatorTests.cs (3 tests) | ✅ green |
| 02-01-03 | 01 | 1 | SESS-03 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SshBackendTests.StopAsync|FullyQualifiedName~SessionCoordinatorTests.StopSession"` | ✅ SshBackendTests.cs + SessionCoordinatorTests.cs + SessionHistoryTests.cs (6 tests) | ✅ green |
| 02-01-04 | 01 | 1 | SESS-04 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SshBackendTests.StartAsync_WithIsFireAndForget"` | ✅ SshBackendTests.cs (1 test) | ✅ green |
| 02-02-01 | 02 | 1 | SESS-05 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SessionHistoryTests|FullyQualifiedName~SessionCoordinatorTests.GetSessionHistory"` | ✅ SessionHistoryTests.cs + SessionCoordinatorTests.cs (6 tests) | ✅ green |
| 02-02-02 | 02 | 1 | AGENT-02 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~ConfigLoaderTests|FullyQualifiedName~ConfigResolution"` | ✅ ConfigLoaderTests.cs (19) + ConfigResolutionServiceTests.cs (5) | ✅ green |
| 02-02-03 | 02 | 1 | AGENT-03 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SanitizationTests"` | ✅ SanitizationTests.cs (42 tests) | ✅ green |
| 02-03-01 | 03 | 2 | AGENT-04 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~ApprovalServiceTests|FullyQualifiedName~SessionCoordinatorApproval"` | ✅ ApprovalServiceTests.cs (11) + SessionCoordinatorApprovalTests.cs (5) + SessionHistoryTests.cs (2) | ✅ green |
| 02-03-02 | 03 | 2 | AGENT-05 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~PermissionFlagTests"` | ✅ PermissionFlagTests.cs (19 tests) | ✅ green |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [x] `tests/AgentHub.Tests/SshBackendTests.cs` — SESS-01, SESS-03, SESS-04 with MockSshHostConnection (11 tests)
- [x] `tests/AgentHub.Tests/SessionCoordinatorTests.cs` — SESS-02 DB-backed listing + SESS-03 stop + SESS-05 history (7 tests)
- [x] `tests/AgentHub.Tests/SessionCoordinatorApprovalTests.cs` — AGENT-04 approval integration (5 tests)
- [x] `tests/AgentHub.Tests/SessionHistoryTests.cs` — SESS-05 query/pagination + AGENT-04 approval resolve (8 tests)
- [x] `tests/AgentHub.Tests/ConfigLoaderTests.cs` — AGENT-02 JSON/YAML/MD parsing + scope merge (19 tests)
- [x] `tests/AgentHub.Tests/ConfigResolutionServiceTests.cs` — AGENT-02 end-to-end pipeline (5 tests)
- [x] `tests/AgentHub.Tests/SanitizationTests.cs` — AGENT-03 extended sanitization + trust tiers (42 tests)
- [x] `tests/AgentHub.Tests/ApprovalServiceTests.cs` — AGENT-04 approval flow + timeout (11 tests)
- [x] `tests/AgentHub.Tests/PermissionFlagTests.cs` — AGENT-05 skip-permissions wiring (19 tests)

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Real SSH session to remote host | SESS-01 | Requires actual remote host with agent installed | Register test host in hosts.json, POST /api/sessions, verify agent starts |
| SSE event delivery to browser | MON-01 | Requires real browser SSE connection | Open /api/events in browser, launch session, verify events stream |
| SessionMonitorService orphan detection | SESS-01 | Real-world heartbeat timing | Start session, disconnect SSH, wait 90s, verify Failed status |

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or Wave 0 dependencies
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers all MISSING references
- [x] No watch-mode flags
- [x] Feedback latency < 15s (actual: ~5s)
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** passed

---

## Validation Audit 2026-03-09

| Metric | Count |
|--------|-------|
| Gaps found | 0 |
| Resolved | 0 |
| Escalated | 0 |
| Total tests | 128 |
| All green | yes |

All 9 requirements (SESS-01 through SESS-05, AGENT-02 through AGENT-05) have automated test coverage. No gaps to fill.
