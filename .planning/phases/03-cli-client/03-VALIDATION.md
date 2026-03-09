---
phase: 3
slug: cli-client
status: complete
nyquist_compliant: true
wave_0_complete: true
created: 2026-03-08
audited: 2026-03-09
---

# Phase 3 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.x (existing project) |
| **Config file** | `tests/AgentHub.Tests/AgentHub.Tests.csproj` |
| **Quick run command** | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~ApiClient|FullyQualifiedName~SseStreamReader|FullyQualifiedName~OutputFormatter|FullyQualifiedName~CliExitCode|FullyQualifiedName~NotificationService"` |
| **Full suite command** | `dotnet test tests/AgentHub.Tests` |
| **Estimated runtime** | ~12 seconds |

---

## Sampling Rate

- **After every task commit:** Run quick command above
- **After every plan wave:** Run `dotnet test tests/AgentHub.Tests`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 12 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 03-01-01 | 01 | 0 | CLI-01 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~ApiClientTests"` | ✅ ApiClientTests.cs (6 tests) | ✅ green |
| 03-01-02 | 01 | 0 | CLI-01 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SseStreamReaderTests"` | ✅ SseStreamReaderTests.cs (5 tests) | ✅ green |
| 03-01-03 | 01 | 0 | CLI-02 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~OutputFormatterTests"` | ✅ OutputFormatterTests.cs (5 tests) | ✅ green |
| 03-01-04 | 01 | 0 | CLI-02 | smoke | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~CliExitCodeTests"` | ✅ CliExitCodeTests.cs (3 tests) | ✅ green |
| 03-02-01 | 02 | 1 | MON-02 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~ApiClientTests.GetHostsAsync"` | ✅ ApiClientTests.cs (1 test) | ✅ green |
| 03-02-02 | 02 | 1 | MON-03 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~NotificationServiceTests"` | ✅ NotificationServiceTests.cs (8 tests) | ✅ green |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [x] `tests/AgentHub.Tests/ApiClientTests.cs` — AgentHubApiClient HTTP calls for CLI-01 (6 tests)
- [x] `tests/AgentHub.Tests/SseStreamReaderTests.cs` — SSE stream parsing, reconnection, Last-Event-ID for CLI-01 (5 tests)
- [x] `tests/AgentHub.Tests/OutputFormatterTests.cs` — JsonFormatter serialization for CLI-02 (5 tests)
- [x] `tests/AgentHub.Tests/CliExitCodeTests.cs` — Exit code mapping (0/1/2) for CLI-02 (3 tests)
- [x] `tests/AgentHub.Tests/NotificationServiceTests.cs` — File persistence, acknowledge, summary for MON-03 (8 tests)
- [x] AgentHub.Cli project reference in test project

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Live watch display updates in real-time | CLI-01 | Requires running terminal with Spectre.Console Live | Start session, run `ah session watch <id>`, verify updates appear |
| Ctrl+C detaches without stopping session | CLI-01 | Requires terminal signal handling | Run `ah run claude "test"`, press Ctrl+C, verify session continues |
| Approval popup overlays watch output | MON-03 | Requires interactive terminal | Watch a session that triggers approval, verify panel appears |
| Terminal bell on notification | MON-03 | Requires audio/visual terminal alert | Run `ah listen`, trigger event, verify bell |
| Host status table rendering | MON-02 | Visual formatting needs human judgment | Run `ah host status`, verify color-coded CPU/memory values |

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or Wave 0 dependencies
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers all MISSING references
- [x] No watch-mode flags
- [x] Feedback latency < 15s (actual: ~12s)
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** passed

---

## Validation Audit 2026-03-09

| Metric | Count |
|--------|-------|
| Gaps found | 4 |
| Resolved | 4 |
| Escalated | 0 |
| Tests created | 1 (CliExitCodeTests.cs, 3 tests) |
| Pre-existing tests discovered | 3 files (SseStreamReaderTests, OutputFormatterTests, NotificationServiceTests) |
| Total tests | 28 |
| All green | yes |

All 4 requirements (CLI-01, CLI-02, MON-02, MON-03) have automated test coverage. 3 of 4 gaps were already covered by pre-existing tests not listed in the original validation map. 1 new test file created for exit code verification.
