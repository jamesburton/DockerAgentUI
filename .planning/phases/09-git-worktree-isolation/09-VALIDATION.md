---
phase: 09
slug: git-worktree-isolation
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-10
---

# Phase 09 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.x with Microsoft.NET.Test.Sdk 17.x |
| **Config file** | `tests/AgentHub.Tests/AgentHub.Tests.csproj` |
| **Quick run command** | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~Worktree" -v q` |
| **Full suite command** | `dotnet test tests/AgentHub.Tests -v q` |
| **Estimated runtime** | ~15 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~Worktree" -v q`
- **After every plan wave:** Run `dotnet test tests/AgentHub.Tests -v q`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 15 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 09-01-01 | 01 | 1 | WKTREE-01 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~WorktreeServiceTests"` | ❌ W0 | ⬜ pending |
| 09-01-02 | 01 | 1 | WKTREE-02 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~WorktreeCleanup"` | ❌ W0 | ⬜ pending |
| 09-01-03 | 01 | 1 | WKTREE-03 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~BranchNameGenerator"` | ❌ W0 | ⬜ pending |
| 09-01-04 | 01 | 1 | WKTREE-04 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~DiffStats"` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `tests/AgentHub.Tests/WorktreeServiceTests.cs` — covers WKTREE-01, WKTREE-02 (create, cleanup, stash-before-remove, error handling)
- [ ] `tests/AgentHub.Tests/BranchNameGeneratorTests.cs` — covers WKTREE-03 (slug generation, edge cases, collision avoidance)
- [ ] `tests/AgentHub.Tests/DiffStatsParserTests.cs` — covers WKTREE-04 (numstat parsing, empty diff, binary files)
- [ ] `tests/AgentHub.Tests/SshBackendWorktreeTests.cs` — covers WKTREE-01 + WKTREE-02 integration (SshBackend lifecycle with worktree flag)

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Worktree creation on real SSH host | WKTREE-01 | Requires live SSH + git repo | Launch session with `--worktree`, verify directory created on host |
| Worktree cleanup on session stop | WKTREE-02 | Requires live session lifecycle | Stop session, verify worktree directory and branch removed |
| Diff stats display in CLI | WKTREE-04 | Visual verification of formatting | Run `ah session diff {id}`, check output format |
| Diff stats display in Web UI | WKTREE-04 | Visual verification of MudBlazor rendering | Open session detail, check diff panel |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 15s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
