---
phase: 3
slug: cli-client
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-08
---

# Phase 3 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.x (existing project) |
| **Config file** | `tests/AgentHub.Tests/AgentHub.Tests.csproj` (existing) |
| **Quick run command** | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~Cli" -x` |
| **Full suite command** | `dotnet test tests/AgentHub.Tests` |
| **Estimated runtime** | ~15 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~Cli" -x`
- **After every plan wave:** Run `dotnet test tests/AgentHub.Tests`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 15 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 03-01-01 | 01 | 0 | CLI-01 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~CliCommandTests" --no-build` | ❌ W0 | ⬜ pending |
| 03-01-02 | 01 | 0 | CLI-02 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~JsonFormatterTests" --no-build` | ❌ W0 | ⬜ pending |
| 03-01-03 | 01 | 0 | CLI-02 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~ExitCodeTests" --no-build` | ❌ W0 | ⬜ pending |
| 03-01-04 | 01 | 0 | MON-03 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SseStreamTests" --no-build` | ❌ W0 | ⬜ pending |
| 03-01-05 | 01 | 0 | MON-03 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~ApprovalHandlerTests" --no-build` | ❌ W0 | ⬜ pending |
| 03-02-01 | 02 | 1 | CLI-01 | integration | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~CliIntegrationTests" --no-build` | ❌ W0 | ⬜ pending |
| 03-02-02 | 02 | 1 | MON-02 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~HostStatusTests" --no-build` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `tests/AgentHub.Tests/Cli/CliCommandTests.cs` — stubs for CLI-01 (command routing, API client calls)
- [ ] `tests/AgentHub.Tests/Cli/JsonFormatterTests.cs` — stubs for CLI-02 (JSON output correctness)
- [ ] `tests/AgentHub.Tests/Cli/ExitCodeTests.cs` — stubs for CLI-02 (exit code mapping)
- [ ] `tests/AgentHub.Tests/Cli/SseStreamTests.cs` — stubs for MON-03 (SSE consumption parsing)
- [ ] `tests/AgentHub.Tests/Cli/ApprovalHandlerTests.cs` — stubs for MON-03 (approval event handling)
- [ ] Add `AgentHub.Cli` project reference to test project
- [ ] Note: Spectre.Console rendering tests use data layer assertions, not terminal output. Use `TestConsole` from Spectre.Console.Testing if needed.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Live watch display updates in real-time | CLI-01 | Requires running terminal with Spectre.Console Live | Start session, run `ah session watch <id>`, verify updates appear |
| Ctrl+C detaches without stopping session | CLI-01 | Requires terminal signal handling | Run `ah run claude "test"`, press Ctrl+C, verify session continues |
| Approval popup overlays watch output | MON-03 | Requires interactive terminal | Watch a session that triggers approval, verify panel appears |
| Terminal bell on notification | MON-03 | Requires audio/visual terminal alert | Run `ah listen`, trigger event, verify bell |
| NO_COLOR env var disables colors | CLI-02 | Requires terminal color inspection | Set NO_COLOR=1, run `ah ls`, verify plain output |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 15s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
