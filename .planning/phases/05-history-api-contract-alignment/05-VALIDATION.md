---
phase: 5
slug: history-api-contract-alignment
status: complete
nyquist_compliant: true
wave_0_complete: true
created: 2026-03-09
validated: 2026-03-09
---

# Phase 5 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit + WebApplicationFactory |
| **Config file** | tests/AgentHub.Tests/AgentHub.Tests.csproj |
| **Quick run command** | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SessionHistoryTests" --no-build -v q` |
| **Full suite command** | `dotnet test tests/AgentHub.Tests --no-build -v q` |
| **Estimated runtime** | ~15 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SessionHistoryTests" --no-build -v q`
- **After every plan wave:** Run `dotnet test tests/AgentHub.Tests --no-build -v q`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** 15 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 05-01-01 | 01 | 1 | SESS-05 | integration | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SessionHistoryTests.GetSessionHistory_ReturnsEventsOrderedByTimestamp"` | ✅ | ✅ green |
| 05-01-02 | 01 | 1 | SESS-05 | integration | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SessionHistoryTests.GetSessionHistory_ReturnsPaginatedEnvelope"` | ✅ | ✅ green |
| 05-01-03 | 01 | 1 | SESS-05 | integration | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SessionHistoryTests.GetSessionHistory_WithKindFilter"` | ✅ | ✅ green |
| 05-01-04 | 01 | 1 | SESS-05 | integration | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SessionHistoryTests.GetSessionHistory_InvalidKind_Returns400"` | ✅ | ✅ green |
| 05-01-05 | 01 | 1 | SESS-05 | integration | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SessionHistoryTests.GetSessionHistory_TotalCountReflectsFilter"` | ✅ | ✅ green |
| 05-01-06 | 01 | 1 | SESS-05 | integration | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SessionHistoryTests.GetSessionHistory_MetaDeserialized"` | ✅ | ✅ green |
| 05-02-01 | 02 | 1 | CLI-01 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~ApiClientTests.GetSessionHistoryAsync_DeserializesEnvelopeWithTotalCount"` | ✅ | ✅ green |
| 05-02-02 | 02 | 1 | WEB-02 | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~DashboardApiClientTests.GetSessionHistoryAsync_DeserializesEnvelopeWithTotalCount"` | ✅ | ✅ green |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [x] New test methods in `tests/AgentHub.Tests/SessionHistoryTests.cs` — envelope shape, pagination, kind filtering, 400 validation, Meta deserialization, totalCount accuracy
- [x] Seed helper update — `SeedSessionWithEvents` sets MetaJson on some events for Meta deserialization tests
- [x] CLI API client unit test — `ApiClientTests.GetSessionHistoryAsync_DeserializesEnvelopeWithTotalCount` added

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Web expandable metadata row | WEB-02 | UI interaction (click-to-expand) | 1. Navigate to /session/{id} 2. Click an event line with metadata 3. Verify meta key-value pairs appear below 4. Click again to collapse |
| CLI metadata display formatting | CLI-01 | Visual output formatting | 1. Run `ah session logs <id>` 2. Verify meta shown for events with metadata 3. Verify no extra lines for events without metadata |

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

Tests added: `ApiClientTests.GetSessionHistoryAsync_DeserializesEnvelopeWithTotalCount`, `ApiClientTests.GetSessionHistoryAsync_SendsCorrectUrlWithQueryParams`
