---
phase: 07-infrastructure-host-inventory
verified: 2026-03-10T12:00:00Z
status: passed
score: 4/4 success criteria verified
must_haves:
  truths:
    - "Database schema includes ParentSessionId, DispatchId, and InventoryJson columns without breaking existing data"
    - "System discovers which agent CLIs are installed on each registered host and displays versions in the dashboard"
    - "System reports available disk space per host alongside existing CPU/memory metrics"
    - "Inventory results are cached and the operator can force a refresh from the UI or API without waiting for the next poll cycle"
  artifacts:
    - path: "src/AgentHub.Orchestration/Data/Entities/HostEntity.cs"
      provides: "InventoryJson column on HostEntity"
    - path: "src/AgentHub.Orchestration/Data/Entities/SessionEntity.cs"
      provides: "ParentSessionId and DispatchId columns"
    - path: "src/AgentHub.Orchestration/Data/AgentHubDbContext.cs"
      provides: "Self-referencing FK configuration for ParentSessionId"
    - path: "src/AgentHub.Orchestration/Migrations/20260309204051_AddInventoryColumns.cs"
      provides: "EF Core migration adding 3 columns and FK"
    - path: "src/AgentHub.Contracts/Models.cs"
      provides: "HostInventory, AgentInfo records; extended HostRecord"
    - path: "src/AgentHub.Orchestration/Data/EntityMappers.cs"
      provides: "InventoryJson deserialization in ToDto/ToEntity"
    - path: "src/AgentHub.Orchestration/Monitoring/HostMetricCache.cs"
      provides: "Thread-safe in-memory cache with partial updates"
    - path: "config/agents.json"
      provides: "Agent CLI definitions for SSH probing"
    - path: "src/AgentHub.Orchestration/Monitoring/HostInventoryPollingService.cs"
      provides: "SSH-based inventory polling background service"
    - path: "src/AgentHub.Orchestration/Monitoring/HostMetricPollingService.cs"
      provides: "Metric polling with HostMetricCache integration"
    - path: "src/AgentHub.Service/Program.cs"
      provides: "DI registration and refresh API endpoints"
    - path: "src/AgentHub.Web/Components/Shared/HostSidebar.razor"
      provides: "Expandable host card with inventory display and refresh"
    - path: "src/AgentHub.Cli/Commands/Host/HostStatusCommand.cs"
      provides: "Agents and Disk columns in CLI host status table"
    - path: "src/AgentHub.Web/Services/DashboardApiClient.cs"
      provides: "RefreshInventoryAsync and RefreshAllInventoryAsync"
    - path: "src/AgentHub.Cli/Api/AgentHubApiClient.cs"
      provides: "RefreshInventoryAsync and RefreshAllInventoryAsync"
    - path: "tests/AgentHub.Tests/InventoryMigrationTests.cs"
      provides: "5 tests for schema and mapper round-trips"
    - path: "tests/AgentHub.Tests/HostMetricCacheTests.cs"
      provides: "7 tests for cache operations and concurrency"
    - path: "tests/AgentHub.Tests/HostInventoryTests.cs"
      provides: "16 tests for probe generation, parsing, version extraction"
  key_links:
    - from: "EntityMappers.cs"
      to: "Models.cs"
      via: "InventoryJson deserialized to HostRecord.Inventory"
    - from: "AgentHubDbContext.cs"
      to: "SessionEntity.cs"
      via: "HasForeignKey ParentSessionId with SetNull"
    - from: "HostInventoryPollingService.cs"
      to: "HostMetricCache.cs"
      via: "_cache.UpdateInventory call"
    - from: "HostMetricPollingService.cs"
      to: "HostMetricCache.cs"
      via: "_cache.UpdateMetrics call"
    - from: "Program.cs"
      to: "HostInventoryPollingService.cs"
      via: "AddSingleton + AddHostedService registration"
    - from: "Program.cs"
      to: "refresh-inventory endpoints"
      via: "MapPost /api/hosts/{hostId}/refresh-inventory and /api/hosts/refresh-inventory"
    - from: "HostSidebar.razor"
      to: "DashboardApiClient"
      via: "RefreshInventoryAsync on refresh button click"
    - from: "HostStatusCommand.cs"
      to: "Models.cs"
      via: "Reads Inventory.Agents and Inventory.DiskFreeGb"
---

# Phase 7: Infrastructure & Host Inventory Verification Report

**Phase Goal:** Operators know exactly what is installed on every host before dispatching work
**Verified:** 2026-03-10T12:00:00Z
**Status:** passed
**Re-verification:** No -- initial verification

## Goal Achievement

### Observable Truths (from ROADMAP Success Criteria)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Database schema includes ParentSessionId, DispatchId, and InventoryJson columns without breaking existing data | VERIFIED | Migration `20260309204051_AddInventoryColumns.cs` adds all 3 columns as nullable TEXT with SetNull FK. `SessionEntity.cs` has ParentSessionId/DispatchId at lines 28-29. `HostEntity.cs` has InventoryJson at line 19. `AgentHubDbContext.cs` configures self-referencing FK at lines 24-27. 154-line test file validates round-trips. |
| 2 | System discovers which agent CLIs are installed on each registered host and displays versions in the dashboard | VERIFIED | `HostInventoryPollingService.cs` (437 lines) generates OS-specific SSH probe commands via `GetInventoryCommand()`, parses JSON output via `ParseInventoryOutput()`, extracts versions via `ExtractVersion()`, resolves capabilities via `ResolveCapabilities()`. `HostSidebar.razor` displays agent names+versions in MudCollapse expandable section (lines 104-115). `HostStatusCommand.cs` shows Agents column with comma-separated names (lines 81-83). 204-line test file with 16 tests covers probing, parsing, version extraction. |
| 3 | System reports available disk space per host alongside existing CPU/memory metrics | VERIFIED | SSH probe commands include `df -BG /` (Linux), `df -g /` (macOS), `Get-PSDrive C` (Windows) for disk. `ParseInventoryOutput` extracts `diskFreeGb` from JSON. `HostSidebar.razor` shows "Disk: X.X GB free" (line 118). `HostStatusCommand.cs` shows Disk column with "X.X GB" (lines 84-86). |
| 4 | Inventory results are cached and the operator can force a refresh from the UI or API without waiting for the next poll cycle | VERIFIED | `HostMetricCache.cs` (60 lines) provides `UpdateInventory()` with `ConcurrentDictionary.AddOrUpdate`. `HostInventoryPollingService` calls `_cache.UpdateInventory()` at line 183. Configurable 60-min interval via `Inventory:PollIntervalMinutes`. Two refresh endpoints in Program.cs: `POST /api/hosts/{hostId}/refresh-inventory` (line 98) and `POST /api/hosts/refresh-inventory` (line 108), both returning `Results.Accepted()`. `HostSidebar.razor` has refresh button calling `DashboardApiClient.RefreshInventoryAsync` (lines 128-132, 154-159). |

**Score:** 4/4 truths verified

### Required Artifacts

| Artifact | Status | Details |
|----------|--------|---------|
| `src/AgentHub.Orchestration/Data/Entities/HostEntity.cs` | VERIFIED | Has `InventoryJson` nullable string property |
| `src/AgentHub.Orchestration/Data/Entities/SessionEntity.cs` | VERIFIED | Has `ParentSessionId` and `DispatchId` nullable string properties |
| `src/AgentHub.Orchestration/Data/AgentHubDbContext.cs` | VERIFIED | Self-referencing FK with `OnDelete(DeleteBehavior.SetNull)` |
| `src/AgentHub.Orchestration/Migrations/20260309204051_AddInventoryColumns.cs` | VERIFIED | 69 lines, adds 3 columns + index + FK in Up(), drops all in Down() |
| `src/AgentHub.Contracts/Models.cs` | VERIFIED | `HostInventory`, `AgentInfo` records; `HostRecord` extended with `Inventory` |
| `src/AgentHub.Orchestration/Data/EntityMappers.cs` | VERIFIED | `ToDto` deserializes `InventoryJson` to `HostInventory`; `ToEntity` serializes back |
| `src/AgentHub.Orchestration/Monitoring/HostMetricCache.cs` | VERIFIED | 60 lines, `ConcurrentDictionary`-backed with `Update`, `UpdateMetrics`, `UpdateInventory`, `Get`, `GetAll` |
| `config/agents.json` | VERIFIED | Defines `claude` and `codex` agents with version patterns and capabilities |
| `src/AgentHub.Orchestration/Monitoring/HostInventoryPollingService.cs` | VERIFIED | 437 lines, full SSH probe implementation with OS-specific commands, JSON parsing, version extraction, capability resolution |
| `src/AgentHub.Orchestration/Monitoring/HostMetricPollingService.cs` | VERIFIED | Injects `HostMetricCache`, calls `_cache.UpdateMetrics()` at line 131 |
| `src/AgentHub.Service/Program.cs` | VERIFIED | Registers `HostMetricCache` singleton, `HostInventoryPollingService` as singleton+hosted, two refresh endpoints |
| `src/AgentHub.Web/Components/Shared/HostSidebar.razor` | VERIFIED | 180 lines, MudCollapse expandable section with agents, disk, git, refresh button |
| `src/AgentHub.Cli/Commands/Host/HostStatusCommand.cs` | VERIFIED | 7 columns including Agents and Disk; null inventory shows "--" |
| `src/AgentHub.Web/Services/DashboardApiClient.cs` | VERIFIED | Has `RefreshInventoryAsync` and `RefreshAllInventoryAsync` |
| `src/AgentHub.Cli/Api/AgentHubApiClient.cs` | VERIFIED | Has `RefreshInventoryAsync` and `RefreshAllInventoryAsync` |
| `tests/AgentHub.Tests/InventoryMigrationTests.cs` | VERIFIED | 154 lines |
| `tests/AgentHub.Tests/HostMetricCacheTests.cs` | VERIFIED | 134 lines |
| `tests/AgentHub.Tests/HostInventoryTests.cs` | VERIFIED | 204 lines |

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| EntityMappers.cs | Models.cs | InventoryJson deserialization | WIRED | Line 87-89: `Deserialize<HostInventory>(entity.InventoryJson)`, passed to `HostRecord` constructor |
| AgentHubDbContext.cs | SessionEntity.cs | ParentSessionId FK | WIRED | Lines 24-27: `HasForeignKey(x => x.ParentSessionId).OnDelete(DeleteBehavior.SetNull)` |
| HostInventoryPollingService.cs | HostMetricCache.cs | UpdateInventory | WIRED | Line 183: `_cache.UpdateInventory(host.HostId, inventory)` |
| HostMetricPollingService.cs | HostMetricCache.cs | UpdateMetrics | WIRED | Line 131: `_cache.UpdateMetrics(host.HostId, cpu, memUsed, memTotal)` |
| Program.cs | HostInventoryPollingService.cs | DI registration | WIRED | `AddSingleton<HostInventoryPollingService>()` + `AddHostedService(sp => sp.GetRequired...)` |
| Program.cs | refresh-inventory | MapPost endpoints | WIRED | Two endpoints at lines 98 and 108, calling `RefreshHostAsync` and `RunOnceAsync`, returning `Results.Accepted()` |
| HostSidebar.razor | DashboardApiClient | RefreshInventoryAsync | WIRED | Line 158: `await Api.RefreshInventoryAsync(hostId)` called on refresh button click |
| HostStatusCommand.cs | Models.cs | Inventory.Agents/DiskFreeGb | WIRED | Lines 81-86: reads `h.Inventory?.Agents` and `h.Inventory?.DiskFreeGb` |

### Requirements Coverage

| Requirement | Source Plan(s) | Description | Status | Evidence |
|-------------|---------------|-------------|--------|----------|
| INFRA-01 | 07-01 | EF Core migration adds ParentSessionId, DispatchId, InventoryJson columns | SATISFIED | Migration file adds all 3 columns; entities have properties; FK configured |
| INFRA-02 | 07-01 | HostMetricCache provides real-time metrics to placement engine | SATISFIED | HostMetricCache singleton with ConcurrentDictionary; both polling services feed it |
| INVT-01 | 07-02, 07-03 | System discovers installed agent CLIs on each host via SSH probing | SATISFIED | OS-specific composite SSH commands in HostInventoryPollingService; agents discovered via `which`/`where.exe` |
| INVT-02 | 07-02, 07-03 | System detects agent CLI versions to prevent incompatible flag dispatch | SATISFIED | ExtractVersion + ResolveCapabilities; versions displayed in UI and CLI |
| INVT-03 | 07-02, 07-03 | System monitors available disk space on hosts via health polling | SATISFIED | `df -BG /` (Linux), `df -g /` (macOS), `Get-PSDrive C` (Windows); displayed in dashboard and CLI |
| INVT-04 | 07-02, 07-03 | Inventory results are cached with configurable TTL and refreshable on demand | SATISFIED | HostMetricCache caching; 60-min configurable interval; two refresh API endpoints; UI refresh button |

No orphaned requirements -- all 6 IDs (INFRA-01, INFRA-02, INVT-01, INVT-02, INVT-03, INVT-04) are claimed by plans and satisfied.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| -- | -- | No TODO/FIXME/PLACEHOLDER/NotImplemented found | -- | -- |

No anti-patterns detected. All monitoring files scanned clean.

### Human Verification Required

### 1. Expandable Host Card Visual Correctness

**Test:** Start the service, open web dashboard, click expand arrow on a host card
**Expected:** Collapsed state shows name, status dot, CPU bar, MEM bar, session chip (unchanged). Expanded state reveals Installed Agents section, Disk free GB, Git version, and refresh button.
**Why human:** Visual layout, spacing, and interaction behavior cannot be verified programmatically.

### 2. Refresh Button Functionality

**Test:** Click the refresh button on an expanded host card
**Expected:** Button disables during refresh, re-enables after. No errors. Inventory data updates if host is reachable.
**Why human:** Requires running service with SSH-reachable hosts to verify end-to-end refresh flow.

### 3. CLI Host Status Table Layout

**Test:** Run `ah host status` with hosts that have inventory data
**Expected:** Table shows 7 columns: Name, OS, CPU%, Memory, Agents, Disk, Sessions. Agents column shows comma-separated names. Disk shows free GB.
**Why human:** Terminal rendering and column alignment need visual confirmation.

### Gaps Summary

No gaps found. All 4 success criteria verified with full artifact existence, substantive implementation, and wiring confirmed. All 6 requirement IDs satisfied. No anti-patterns detected.

---

_Verified: 2026-03-10T12:00:00Z_
_Verifier: Claude (gsd-verifier)_
