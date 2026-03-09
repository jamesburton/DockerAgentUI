# Phase 7: Infrastructure & Host Inventory - Research

**Researched:** 2026-03-09
**Domain:** EF Core schema migration, SSH-based host inventory discovery, in-memory caching, MudBlazor UI expansion
**Confidence:** HIGH

## Summary

Phase 7 adds three capabilities to AgentHub: (1) EF Core schema additions (ParentSessionId, DispatchId on Sessions; InventoryJson on Hosts), (2) an in-memory HostMetricCache singleton for the placement engine, and (3) SSH-based agent CLI discovery with version detection, disk space monitoring, and cached inventory with on-demand refresh. All three build directly on established codebase patterns.

The codebase already has a well-structured HostMetricPollingService that SSHs into hosts every 30 seconds for CPU/memory. The new HostInventoryPollingService follows the identical BackgroundService pattern but runs at 1-hour intervals. The schema migration is straightforward -- three nullable columns using the same AddColumn pattern as the existing AddHostMetricColumns migration. The HostMetricCache is a new ConcurrentDictionary-based singleton that both polling services update.

**Primary recommendation:** Mirror the HostMetricPollingService pattern exactly for inventory polling, use a single composite SSH command per host that returns JSON, and introduce HostMetricCache as the single read-path for the placement engine.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- Rich capability profile: InventoryJson stores agents (name, version, path, capabilities), diskFreeGb, and gitVersion
- Agent capabilities derived from config-driven version mapping (not runtime probing)
- Disk space lives inside InventoryJson (not a separate DB column) -- refreshes on inventory cycle
- Git version included in inventory (Phase 9 reads it for worktree support verification)
- Single composite SSH command per host -- probes all agents + git + disk in one call, returns structured JSON
- Probe all agents defined in agents.json by default on every host
- Per-host overrides supported: skip certain probes or specify custom CLI paths
- Path lookup: default to `which` (Linux/macOS) / `where` (Windows), custom path in host config takes precedence
- OS-appropriate commands following established HostMetricPollingService pattern (Windows-first priority)
- Expandable host card in HostSidebar: click to reveal installed agents with versions, disk space, and git version
- Collapsed state unchanged (name, status, CPU bar, memory bar, session count)
- Agent display: name + version only (capabilities are internal for placement engine, not shown in UI)
- Per-host refresh button in the expanded inventory section
- Add Agents and Disk columns to existing `ah host status` table
- Agents column shows comma-separated names (e.g., "claude,codex")
- Disk column shows free GB
- Separate HostInventoryPollingService with 1-hour default TTL (configurable)
- HostMetricPollingService stays at 30s for CPU/memory (unchanged)
- On-demand refresh: per-host (POST /api/hosts/{hostId}/refresh-inventory) and refresh-all (POST /api/hosts/refresh-inventory)
- Cache auto-invalidated when system applies changes to a host
- In-memory singleton cache (ConcurrentDictionary<string, HostSnapshot>)
- Holds latest metrics + inventory per host, updated by both polling services
- Placement engine reads from cache instead of DB
- Exposes: Update(), Get(hostId), GetAll()
- Add ParentSessionId (string?, nullable FK to Sessions) to SessionEntity
- Add DispatchId (string?, nullable) to SessionEntity
- Add InventoryJson (string?, nullable) to HostEntity
- Single EF Core migration, non-breaking (all nullable columns)

### Claude's Discretion
- agents.json config file location and format details
- Composite SSH probe script implementation per OS
- HostSnapshot record shape and cache eviction details
- Expandable card animation/transition in MudBlazor
- Inventory SSE event design (if needed for real-time cache updates)
- Error handling for SSH probe failures (partial results vs full failure)

### Deferred Ideas (OUT OF SCOPE)
None -- discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| INFRA-01 | EF Core migration adds ParentSessionId, DispatchId, InventoryJson columns | Existing migration pattern (AddHostMetricColumns) provides exact template; all nullable columns, non-breaking |
| INFRA-02 | HostMetricCache provides real-time metrics to placement engine | ConcurrentDictionary singleton pattern; replaces current DB round-trip in SshBackend.GetInventoryAsync |
| INVT-01 | System discovers installed agent CLIs on each host via SSH probing | Composite SSH command per OS following HostMetricPollingService pattern; agents.json defines probes |
| INVT-02 | System detects agent CLI versions to prevent incompatible flag dispatch | Version flags per agent (e.g., `claude --version`); parsed from composite SSH output JSON |
| INVT-03 | System monitors available disk space on hosts via health polling | Disk space included in composite SSH probe (PowerShell Get-PSDrive / df -BG); stored in InventoryJson |
| INVT-04 | Inventory results are cached with configurable TTL and refreshable on demand | HostInventoryPollingService at 1hr + POST refresh endpoints + HostMetricCache singleton |
</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| EF Core (SQLite) | 10.0.2 | Schema migration, DB access | Already in use; AddColumn migration pattern proven |
| SSH.NET (Renci.SshNet) | existing | SSH command execution | Already used by SshHostConnection; no new dependency |
| System.Text.Json | built-in | JSON serialization for InventoryJson | Already used throughout (EntityMappers, HostSeedingService) |
| ConcurrentDictionary | built-in | Thread-safe in-memory cache | Standard .NET concurrent collection; fits singleton pattern |
| MudBlazor | existing | Expandable host cards in sidebar | Already used for HostSidebar; MudCollapse component available |
| Spectre.Console | existing | CLI table columns | Already used by HostStatusCommand; just add columns |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| IConfiguration | built-in | Inventory poll interval, agents.json path | Config binding for HostInventoryPollingService |
| IHostedService | built-in | Background inventory polling | Same pattern as HostMetricPollingService |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| ConcurrentDictionary cache | IMemoryCache | IMemoryCache adds sliding/absolute expiration but is heavier; ConcurrentDictionary is simpler and both polling services explicitly control updates |
| Single composite SSH command | Separate SSH calls per probe | Multiple SSH connections per host would be slow and wasteful; composite is explicitly locked |

## Architecture Patterns

### Recommended Project Structure
```
src/AgentHub.Orchestration/
  Monitoring/
    HostMetricPollingService.cs      # existing, unchanged
    HostInventoryPollingService.cs   # NEW - 1hr SSH inventory probe
    HostMetricCache.cs               # NEW - ConcurrentDictionary singleton
  Data/
    Entities/
      HostEntity.cs                  # ADD InventoryJson property
      SessionEntity.cs              # ADD ParentSessionId, DispatchId
    AgentHubDbContext.cs             # ADD ParentSessionId FK config
    EntityMappers.cs                 # EXTEND ToDto/ToEntity for new fields
    Migrations/
      *_AddInventoryColumns.cs       # NEW migration
  Placement/
    SimplePlacementEngine.cs         # INJECT HostMetricCache (future phase)

config/
  agents.json                        # NEW - agent CLI definitions

src/AgentHub.Contracts/
  Models.cs                          # EXTEND HostRecord with inventory fields

src/AgentHub.Web/
  Components/Shared/
    HostSidebar.razor                # ADD expandable inventory section
  Services/
    DashboardApiClient.cs            # ADD RefreshInventoryAsync methods

src/AgentHub.Cli/
  Commands/Host/
    HostStatusCommand.cs             # ADD Agents, Disk columns
  Api/
    AgentHubApiClient.cs             # ADD RefreshInventoryAsync methods

src/AgentHub.Service/
  Program.cs                         # REGISTER HostInventoryPollingService, HostMetricCache, endpoints
```

### Pattern 1: BackgroundService Polling (follow HostMetricPollingService exactly)
**What:** Long-running service that polls hosts on a timer
**When to use:** For the HostInventoryPollingService
**Example:**
```csharp
// Source: existing HostMetricPollingService.cs (lines 19-68)
public sealed class HostInventoryPollingService : BackgroundService
{
    private readonly IDbContextFactory<AgentHubDbContext> _dbFactory;
    private readonly ISshHostConnectionFactory _connectionFactory;
    private readonly HostMetricCache _cache;
    private readonly DurableEventService _events;
    private readonly ILogger<HostInventoryPollingService> _logger;
    private readonly TimeSpan _pollInterval;
    private readonly string _sshKeyPath;
    private readonly string _sshUsername;

    // Constructor takes IConfiguration for "Inventory:PollIntervalMinutes" (default 60)

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Same while-loop + try/catch + Task.Delay pattern
        // RunOnceAsync() exposed for testing, same as HostMetricPollingService
    }

    // Public RunOnceAsync for unit testing
    // Private PollHostAsync per host
    // Static GetInventoryCommand(string os) returning OS-specific composite probe
    // Static ParseInventoryOutput(string json) returning structured inventory
}
```

### Pattern 2: HostMetricCache Singleton
**What:** Thread-safe in-memory cache updated by both polling services
**When to use:** Single source of truth for placement decisions
**Example:**
```csharp
// New singleton registered in Program.cs
public sealed class HostMetricCache
{
    private readonly ConcurrentDictionary<string, HostSnapshot> _cache = new();

    public void Update(string hostId, HostSnapshot snapshot) => _cache[hostId] = snapshot;
    public HostSnapshot? Get(string hostId) => _cache.GetValueOrDefault(hostId);
    public IReadOnlyList<HostSnapshot> GetAll() => _cache.Values.ToList();
}

// HostSnapshot holds combined metrics + inventory
public sealed record HostSnapshot(
    string HostId,
    double? CpuPercent,
    long? MemUsedMb,
    long? MemTotalMb,
    HostInventory? Inventory,
    DateTimeOffset? MetricsUpdatedUtc,
    DateTimeOffset? InventoryUpdatedUtc);

// HostInventory is the deserialized InventoryJson
public sealed record HostInventory(
    List<AgentInfo> Agents,
    double? DiskFreeGb,
    string? GitVersion);

public sealed record AgentInfo(
    string Name,
    string? Version,
    string? Path,
    List<string>? Capabilities);
```

### Pattern 3: agents.json Configuration
**What:** Config file defining known agent CLIs, their version flags, and capability mappings
**When to use:** Drives the composite SSH probe
**Example:**
```json
// config/agents.json
{
  "agents": [
    {
      "name": "claude",
      "versionFlag": "--version",
      "versionPattern": "claude-code/(\\d+\\.\\d+\\.\\d+)",
      "capabilities": {
        "1.0.0+": ["code-generation", "file-edit", "bash"]
      }
    },
    {
      "name": "codex",
      "versionFlag": "--version",
      "versionPattern": "(\\d+\\.\\d+\\.\\d+)",
      "capabilities": {
        "0.1.0+": ["code-generation"]
      }
    }
  ]
}
```

### Pattern 4: EF Core AddColumn Migration
**What:** Non-breaking schema addition with nullable columns
**When to use:** For INFRA-01
**Example:**
```csharp
// Source: existing 20260309153831_AddHostMetricColumns.cs
// Follow identical pattern: Up() adds columns, Down() drops them
protected override void Up(MigrationBuilder migrationBuilder)
{
    // Sessions table
    migrationBuilder.AddColumn<string>(
        name: "ParentSessionId", table: "Sessions", type: "TEXT", nullable: true);
    migrationBuilder.AddColumn<string>(
        name: "DispatchId", table: "Sessions", type: "TEXT", nullable: true);
    // Hosts table
    migrationBuilder.AddColumn<string>(
        name: "InventoryJson", table: "Hosts", type: "TEXT", nullable: true);
}
```

### Pattern 5: MudCollapse for Expandable Host Card
**What:** MudBlazor's MudCollapse component for show/hide sections
**When to use:** Expandable inventory details in HostSidebar
**Example:**
```razor
@* Inside the host card foreach loop, after the MEM bar *@
<MudCollapse Expanded="@(ExpandedHostId == host.HostId)">
    <MudDivider Class="my-1" />
    <MudText Typo="Typo.caption" Class="mb-1"><b>Installed Agents</b></MudText>
    @if (host.Inventory?.Agents is not null)
    {
        @foreach (var agent in host.Inventory.Agents)
        {
            <MudText Typo="Typo.caption">@agent.Name @agent.Version</MudText>
        }
    }
    @if (host.Inventory?.DiskFreeGb.HasValue == true)
    {
        <MudText Typo="Typo.caption">Disk: @($"{host.Inventory.DiskFreeGb:F1}") GB free</MudText>
    }
    <MudIconButton Icon="@Icons.Material.Filled.Refresh" Size="Size.Small"
                   OnClick="() => RefreshInventory(host.HostId)" />
</MudCollapse>
```

### Anti-Patterns to Avoid
- **Separate SSH connections per probe:** Always use composite command. Multiple SSH round-trips per host waste time and connections.
- **Storing disk space in a separate DB column:** Locked decision -- disk space lives in InventoryJson, refreshes on inventory cycle (1hr not 30s).
- **Querying DB from placement engine:** HostMetricCache exists to eliminate DB round-trips during dispatch. Never bypass the cache.
- **Runtime probing for capabilities:** Capabilities come from agents.json version mapping, not from running agent commands to discover features.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Thread-safe cache | Custom locking | ConcurrentDictionary<string, HostSnapshot> | Battle-tested concurrent collection; no lock management needed |
| SSH execution | Custom SSH client | Renci.SshNet via ISshHostConnectionFactory | Already wrapped and testable; handles keys, keepalive, reconnection |
| JSON column serialization | Manual string building | System.Text.Json (existing JsonOpts pattern) | Already used in EntityMappers, HostSeedingService; consistent serialization |
| OS-specific command building | If/else chains inline | Static method per OS (GetInventoryCommand) | HostMetricPollingService.GetMetricCommand already establishes this pattern |
| Schema migration | Raw SQL scripts | EF Core migrations via `dotnet ef` | Project already uses EF Core migrations exclusively |
| Expandable UI panel | Custom JS toggle | MudBlazor MudCollapse component | MudBlazor already in use; MudCollapse handles animation/state |

## Common Pitfalls

### Pitfall 1: SSH Command Quoting on Windows
**What goes wrong:** PowerShell commands with nested quotes break when passed through SSH
**Why it happens:** SSH wraps the command in a shell, adding another layer of escaping
**How to avoid:** Follow the exact escaping pattern in HostMetricPollingService.GetMetricCommand for Windows (backslash-escaped inner quotes in PowerShell -Command)
**Warning signs:** Commands work locally but fail via SSH; parse errors in probe output

### Pitfall 2: HostMetricCache Stale Data on Startup
**What goes wrong:** Cache is empty when service starts; placement engine gets no hosts
**Why it happens:** ConcurrentDictionary starts empty; first inventory poll hasn't run yet
**How to avoid:** Pre-populate cache from DB on startup (in HostMetricCache constructor or an initialization method). Also ensure the SshBackend.GetInventoryAsync falls back to DB if cache is empty.
**Warning signs:** First session launch after restart fails with "No eligible node found"

### Pitfall 3: InventoryJson Deserialization Null Safety
**What goes wrong:** NullReferenceException when reading inventory fields
**Why it happens:** InventoryJson is nullable in DB; old hosts won't have it; failed probes leave it null
**How to avoid:** Always null-check InventoryJson before deserializing. HostRecord DTO should have nullable inventory fields. UI must handle null inventory gracefully (show "--" or "Not probed").
**Warning signs:** Dashboard crash when viewing a host that hasn't been probed yet

### Pitfall 4: Composite SSH Probe Partial Failures
**What goes wrong:** One agent not found (e.g., `which codex` fails) kills the entire probe
**Why it happens:** Shell script exits on first error if using `set -e` or `&&` chaining
**How to avoid:** Use `||` with fallback values in the probe script. Each agent probe should be independent -- capture "not found" as null/empty, not as a script failure. Return partial results.
**Warning signs:** All agents show as missing because one probe failed

### Pitfall 5: EF Core FK Configuration for ParentSessionId
**What goes wrong:** EF Core doesn't know ParentSessionId is a self-referencing FK
**Why it happens:** Just adding a string property doesn't create a relationship
**How to avoid:** Configure in OnModelCreating: `e.HasOne<SessionEntity>().WithMany().HasForeignKey(x => x.ParentSessionId).OnDelete(DeleteBehavior.SetNull)`. Use SetNull to avoid cascade-deleting child sessions when parent is deleted.
**Warning signs:** No FK constraint in migration; orphaned child sessions after parent deletion

### Pitfall 6: SQLite Text Column Size for InventoryJson
**What goes wrong:** Very large InventoryJson values if many agents installed
**Why it happens:** SQLite TEXT columns have no practical size limit, but large JSON payloads slow serialization
**How to avoid:** Keep inventory lean (name, version, path, capabilities list). Don't store raw CLI output. Typical inventory should be under 2KB.
**Warning signs:** Slow host list API response; large DB file growth

## Code Examples

### Composite SSH Probe Command (Windows)
```powershell
# Source: pattern derived from HostMetricPollingService.GetMetricCommand for Windows
powershell -Command "
  $result = @{ agents = @(); diskFreeGb = $null; gitVersion = $null }
  # Agents from agents.json (injected at build time)
  foreach ($agent in @('claude','codex')) {
    $path = (where.exe $agent 2>$null | Select-Object -First 1)
    if ($path) {
      $ver = & $path --version 2>$null
      $result.agents += @{ name = $agent; version = $ver; path = $path }
    }
  }
  # Disk
  $disk = Get-PSDrive C | Select-Object -ExpandProperty Free
  $result.diskFreeGb = [math]::Round($disk / 1GB, 1)
  # Git
  $gitPath = (where.exe git 2>$null | Select-Object -First 1)
  if ($gitPath) { $result.gitVersion = (& git --version 2>$null) }
  $result | ConvertTo-Json -Compress
"
```

### Composite SSH Probe Command (Linux)
```bash
# Source: pattern derived from HostMetricPollingService.GetMetricCommand for Linux
bash -c '
  agents="[]"
  for name in claude codex; do
    path=$(which $name 2>/dev/null)
    if [ -n "$path" ]; then
      ver=$($path --version 2>/dev/null | head -1)
      agents=$(echo "$agents" | jq --arg n "$name" --arg v "$ver" --arg p "$path" ". + [{\"name\":\$n,\"version\":\$v,\"path\":\$p}]")
    fi
  done
  disk=$(df -BG / 2>/dev/null | awk "NR==2 {gsub(/G/,\"\",\$4); print \$4}")
  gitver=$(git --version 2>/dev/null)
  echo "{\"agents\":$agents,\"diskFreeGb\":$disk,\"gitVersion\":\"$gitver\"}"
'
```

### On-Demand Refresh API Endpoint
```csharp
// Source: pattern from existing Program.cs endpoint registration
app.MapPost("/api/hosts/{hostId}/refresh-inventory", async (
    string hostId,
    HostInventoryPollingService inventoryService,
    CancellationToken ct) =>
{
    await inventoryService.RefreshHostAsync(hostId, ct);
    return Results.Accepted();
});

app.MapPost("/api/hosts/refresh-inventory", async (
    HostInventoryPollingService inventoryService,
    CancellationToken ct) =>
{
    await inventoryService.RunOnceAsync(ct);
    return Results.Accepted();
});
```

### Extended HostRecord Contract
```csharp
// Source: existing HostRecord in Contracts/Models.cs -- extend with inventory
public sealed record HostRecord(
    string HostId,
    string DisplayName,
    string Backend,
    string Os,
    bool Enabled,
    bool AllowSsh,
    Dictionary<string, string>? Labels = null,
    string? Address = null,
    double? CpuPercent = null,
    long? MemUsedMb = null,
    long? MemTotalMb = null,
    // Phase 7 additions
    HostInventory? Inventory = null);
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Backend.GetInventoryAsync() builds NodeCapability from DB | HostMetricCache provides cached snapshot | Phase 7 | Placement engine avoids DB round-trip on every dispatch |
| No agent discovery | SSH probe discovers installed CLIs | Phase 7 | Operators see what's available before dispatching |
| No disk monitoring | Disk included in inventory probe | Phase 7 | Prevents dispatch to hosts with low disk space |

## Open Questions

1. **jq dependency on remote hosts for Linux probe**
   - What we know: The Linux composite probe example uses jq for JSON building
   - What's unclear: Whether target hosts will always have jq installed
   - Recommendation: Build JSON with printf/echo string concatenation instead of jq to avoid dependency. Use jq only if available, fallback to manual JSON construction.

2. **agents.json reload without restart**
   - What we know: agents.json drives which CLIs to probe; HostSeedingService reads hosts.json only at startup
   - What's unclear: Whether agents.json should be watched for changes or read on each poll cycle
   - Recommendation: Read agents.json on each poll cycle (once per hour is cheap). Avoids file watcher complexity and allows hot config updates.

3. **HostInventoryPollingService registration pattern**
   - What we know: HostMetricPollingService uses AddHostedService<T>() (line 59 of Program.cs). But on-demand refresh needs access to the service instance.
   - What's unclear: How to both register as hosted service AND inject into endpoints
   - Recommendation: Register as singleton first, then add as hosted service: `builder.Services.AddSingleton<HostInventoryPollingService>(); builder.Services.AddHostedService(sp => sp.GetRequiredService<HostInventoryPollingService>());`

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xunit 2.9.x + Microsoft.NET.Test.Sdk 17.x |
| Config file | tests/AgentHub.Tests/AgentHub.Tests.csproj |
| Quick run command | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~AgentHub.Tests" --no-build -v q` |
| Full suite command | `dotnet test tests/AgentHub.Tests -v q` |

### Phase Requirements to Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| INFRA-01 | Migration adds 3 nullable columns without breaking existing data | integration | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~InventoryMigrationTests" -x` | No - Wave 0 |
| INFRA-02 | HostMetricCache Update/Get/GetAll thread-safe operations | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~HostMetricCacheTests" -x` | No - Wave 0 |
| INVT-01 | SSH probe discovers agent CLIs on each host | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~HostInventoryTests" -x` | No - Wave 0 |
| INVT-02 | Version detection parses CLI output correctly | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~HostInventoryTests" -x` | No - Wave 0 |
| INVT-03 | Disk space parsed from probe output | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~HostInventoryTests" -x` | No - Wave 0 |
| INVT-04 | Inventory cached with TTL, refreshable on demand | unit | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~HostInventoryTests" -x` | No - Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test tests/AgentHub.Tests --no-build -v q`
- **Per wave merge:** `dotnet test tests/AgentHub.Tests -v q`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `tests/AgentHub.Tests/HostMetricCacheTests.cs` -- covers INFRA-02 (Update, Get, GetAll, concurrent access)
- [ ] `tests/AgentHub.Tests/HostInventoryTests.cs` -- covers INVT-01 through INVT-04 (probe command generation, output parsing, version extraction, disk parsing, cache TTL)
- [ ] `tests/AgentHub.Tests/InventoryMigrationTests.cs` -- covers INFRA-01 (entity with new columns round-trips through InMemory DbContext)

## Sources

### Primary (HIGH confidence)
- Existing codebase: HostMetricPollingService.cs -- exact pattern to follow for HostInventoryPollingService
- Existing codebase: AddHostMetricColumns migration -- exact pattern for new migration
- Existing codebase: EntityMappers.cs, HostEntity.cs, SessionEntity.cs, HostRecord -- files to extend
- Existing codebase: SshHostConnection.cs -- SSH execution infrastructure already in place
- Existing codebase: Program.cs -- service registration patterns

### Secondary (MEDIUM confidence)
- MudBlazor MudCollapse component -- standard component for expandable sections, project already uses MudBlazor
- ConcurrentDictionary for cache -- well-established .NET pattern for thread-safe dictionaries

### Tertiary (LOW confidence)
- Composite SSH probe scripts -- exact syntax needs testing per OS; Windows PowerShell quoting through SSH is fragile
- jq availability on target hosts -- assumed available on Linux but not guaranteed

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- all libraries already in use, no new dependencies
- Architecture: HIGH -- directly follows established patterns in codebase
- Pitfalls: HIGH -- derived from actual codebase analysis (SSH quoting, null safety, FK config)
- SSH probe scripts: MEDIUM -- OS-specific commands need runtime validation

**Research date:** 2026-03-09
**Valid until:** 2026-04-09 (stable codebase, no fast-moving dependencies)
