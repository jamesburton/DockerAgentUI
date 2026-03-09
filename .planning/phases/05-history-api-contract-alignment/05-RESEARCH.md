# Phase 5: History API Contract Alignment - Research

**Researched:** 2026-03-09
**Domain:** ASP.NET Minimal API contract alignment, pagination, client-side envelope deserialization
**Confidence:** HIGH

## Summary

Phase 5 is a gap closure phase. The `/api/sessions/{id}/history` endpoint currently returns anonymous objects with raw `MetaJson` strings and no pagination or filtering. All consumer code (CLI `AgentHubApiClient`, Web `DashboardApiClient`, `SessionLogsCommand`, `SessionDetail.razor`) already sends `page`, `pageSize`, and `kind` query parameters but the server ignores them, and both clients currently deserialize the response as a bare `List<SessionEvent>` rather than the `{items, totalCount}` envelope established by `GET /api/sessions`.

The fix is surgical: rewrite the endpoint in Program.cs (~25 lines), update both API clients to deserialize the envelope response (same pattern already used for `GetSessionsAsync`), and add expandable metadata display to `TerminalOutput.razor`. The `EntityMappers.ToDto()` extension method and `SessionEvent` DTO already exist and are correct -- they just are not being used.

**Primary recommendation:** Rewrite the history endpoint to use `EntityMappers.ToDto()` with in-memory sort/filter/paginate (matching the Phase 2 pattern), wrap in `{items, totalCount}` envelope, update both client deserializers, and add metadata UI to TerminalOutput.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- Use existing `EntityMappers.ToDto()` to convert `SessionEventEntity` to `SessionEvent` DTO -- no anonymous objects
- Kind serialized as string name (e.g. "StdOut", "StdErr") -- matches existing camelCase JSON serialization and CLI color-coding
- Meta dictionary properly deserialized from MetaJson -- clients receive `Dictionary<string, string>?` not raw JSON string
- Web session detail: event metadata displayed via expandable row (click event line to see meta key-value pairs)
- Response wrapped in envelope: `{items: [...], totalCount: N}` -- consistent with existing `GET /api/sessions` pattern (Phase 2 decision)
- Default page size: 100 events (matches Web client's existing batch size)
- In-memory sorting maintained (Phase 2 decision -- SQLite DateTimeOffset ORDER BY limitation)
- Comma-separated multi-value filter supported: `?kind=StdOut,StdErr`
- Case-insensitive parsing: accept "stdout", "STDOUT", "StdOut" -- use `Enum.TryParse` with `ignoreCase: true`
- Invalid kind values return 400 Bad Request with error message
- totalCount reflects filtered count (after kind filter applied), not total unfiltered events
- Full test coverage: response shape, pagination math, kind filtering, totalCount accuracy
- Update existing `SessionHistoryTests.cs` in place (don't create separate test class)
- Update client tests: verify both CLI `AgentHubApiClient` and Web `DashboardApiClient` correctly handle the `{items, totalCount}` envelope response

### Claude's Discretion
- Whether to include DB auto-increment Id in the DTO response (evaluate based on SSE replay patterns and client needs)
- CLI metadata display approach (inline, separate line, or verbose-only)
- Out-of-range page behavior (empty result vs error)
- Max page size cap value
- Client-side changes needed to handle envelope response (both CLI and Web API clients currently expect bare arrays)

### Deferred Ideas (OUT OF SCOPE)
None -- discussion stayed within phase scope
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| SESS-05 | User can review past session history with stored output and outcome | Endpoint rewrite with ToDto(), pagination, kind filter, Meta deserialization |
| CLI-01 | CLI client supports launching, monitoring, and managing sessions | CLI AgentHubApiClient updated for envelope response; SessionLogsCommand already handles pagination correctly once client returns proper data |
| WEB-02 | Web dashboard streams real-time agent output inline | Web DashboardApiClient updated for envelope response; SessionDetail pagination loop updated; TerminalOutput gets expandable meta rows |
</phase_requirements>

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| ASP.NET Minimal APIs | .NET 10 | History endpoint | Already in use for all API endpoints |
| EF Core + SQLite | 10.x | Event query | Already configured with Pool + Factory dual registration |
| System.Text.Json | built-in | Serialization | JsonSerializerDefaults.Web (camelCase) used throughout |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| MudBlazor | existing | Expandable metadata UI | TerminalOutput metadata display |
| Spectre.Console | existing | CLI metadata rendering | SessionLogsCommand event display |
| xUnit + WebApplicationFactory | existing | Integration tests | SessionHistoryTests |

### Alternatives Considered
None -- this phase uses only existing libraries and patterns.

## Architecture Patterns

### Pattern 1: Envelope Response (established Phase 2)
**What:** API returns `{items: T[], totalCount: int}` for paginated endpoints
**When to use:** All list endpoints with pagination
**Example:**
```csharp
// Source: Program.cs line 106, existing GET /api/sessions pattern
app.MapGet("/api/sessions", async (..., int? skip, int? take, string? state, CancellationToken ct) =>
{
    var (items, totalCount) = await coordinator.GetSessionHistoryAsync(...);
    return Results.Json(new { items, totalCount });
});
```

### Pattern 2: In-Memory Sort + Paginate (established Phase 2)
**What:** Load all matching entities, sort in-memory, then paginate via Skip/Take
**When to use:** Any query involving DateTimeOffset ordering on SQLite
**Why:** SQLite DateTimeOffset ORDER BY limitation (Phase 2 decision 02-04)
**Example:**
```csharp
var rawEvents = await db.Events
    .Where(e => e.SessionId == sessionId)
    .ToListAsync(ct);

var sorted = rawEvents
    .OrderBy(e => e.TsUtc)
    .Where(e => /* kind filter */)
    .ToList();

var totalCount = sorted.Count;
var items = sorted.Skip((page - 1) * pageSize).Take(pageSize)
    .Select(e => e.ToDto())
    .ToList();

return Results.Json(new { items, totalCount });
```

### Pattern 3: Client-Side Envelope Deserialization (established Phase 2)
**What:** Clients define internal `record` for typed deserialization of `{items, totalCount}`
**Example (already in both clients for sessions):**
```csharp
// Source: AgentHubApiClient.cs line 103
internal sealed record SessionListResponse(List<SessionSummary> Items, int TotalCount);

// Usage pattern:
var result = await response.Content.ReadFromJsonAsync<SessionListResponse>(s_json, ct)
    ?? throw new InvalidOperationException("...");
return (result.Items, result.TotalCount);
```

### Anti-Patterns to Avoid
- **Anonymous objects in API responses:** The current endpoint uses `new { e.Id, e.SessionId, Kind = e.Kind.ToString(), ... }` -- this breaks typed deserialization and leaks raw MetaJson strings to clients. Use `EntityMappers.ToDto()`.
- **Returning bare arrays from paginated endpoints:** Both clients already send page/pageSize but currently expect `List<SessionEvent>` back. This breaks pagination metadata. Must use envelope.
- **SQL ORDER BY on DateTimeOffset columns:** SQLite stores these as strings and sorts lexicographically. Always sort in-memory after loading.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Entity-to-DTO mapping | Manual property copying | `EntityMappers.ToDto()` | Already exists, handles MetaJson deserialization correctly |
| JSON enum serialization | Custom converters | `JsonSerializerDefaults.Web` | Built-in camelCase handles enum name serialization |
| Enum parsing from query string | Manual string matching | `Enum.TryParse<SessionEventKind>(value, ignoreCase: true, out var kind)` | Handles case insensitivity, returns false for invalid |

## Common Pitfalls

### Pitfall 1: Bare Array vs Envelope Client Deserialization
**What goes wrong:** CLI `GetSessionHistoryAsync` currently deserializes as `List<SessionEvent>` directly. After server returns `{items, totalCount}` envelope, this will throw a `JsonException` at runtime.
**Why it happens:** Both clients were written expecting bare arrays but the endpoint contract is changing.
**How to avoid:** Update both `AgentHubApiClient.GetSessionHistoryAsync` and `DashboardApiClient.GetSessionHistoryAsync` to return `(List<SessionEvent> Items, int TotalCount)` using an internal record, mirroring the existing `GetSessionsAsync` pattern.
**Warning signs:** `System.Text.Json.JsonException: The JSON value could not be converted to System.Collections.Generic.List`

### Pitfall 2: Pagination Batch Detection in SessionLogsCommand
**What goes wrong:** `LoadHistoryAsync` uses `batch.Count < pageSize` to detect the last page. After switching to envelope, `batch` is now extracted from `items` array -- must use `totalCount` or keep the same `items.Count < pageSize` check.
**Why it happens:** The pagination loop logic depends on the return type of `GetSessionHistoryAsync`.
**How to avoid:** Return `(List<SessionEvent>, int TotalCount)` from the client method. The caller can check either `items.Count < pageSize` (still works) or use `totalCount` for a cleaner approach.

### Pitfall 3: Web SessionDetail Pagination Loop
**What goes wrong:** `SessionDetail.razor` line 123 calls `GetSessionHistoryAsync` and checks `batch.Count == 100`. After the method signature changes to return a tuple, this code won't compile.
**How to avoid:** Update the pagination loop to destructure the tuple: `var (batch, total) = await Api.GetSessionHistoryAsync(...)`.

### Pitfall 4: Kind Filter Before vs After Count
**What goes wrong:** If totalCount is calculated before applying the kind filter, clients get wrong pagination math.
**Why it happens:** Order of operations in LINQ pipeline.
**How to avoid:** Per locked decision: totalCount reflects filtered count. Apply kind filter first, then count, then paginate.

### Pitfall 5: SessionEvent DTO Missing Id
**What goes wrong:** The existing `SessionEvent` record does not include the DB auto-increment `Id` field. SSE replay uses `SseItem.EventId` set to this Id (decision 01-02). If follow mode needs Last-Event-ID continuity, the Id must be available.
**Why it happens:** The DTO was designed without considering replay continuation.
**How to avoid:** This is flagged as Claude's discretion. The CLI follow mode already extracts `eventId` from `Meta` dictionary (line 63 of SessionLogsCommand.cs). If the endpoint populates Meta with eventId (or the DurableEventService already does so when emitting), no DTO change needed. Verify the existing SSE event emission path.

## Code Examples

### History Endpoint Rewrite (Server)
```csharp
// Source: Program.cs, replacing lines 116-140
app.MapGet("/api/sessions/{sessionId}/history", async (
    string sessionId,
    IUserContext user,
    ISessionCoordinator coordinator,
    IDbContextFactory<AgentHubDbContext> dbFactory,
    int? page, int? pageSize, string? kind,
    CancellationToken ct) =>
{
    var s = await coordinator.GetSessionAsync(sessionId, user.UserId, ct);
    if (s is null) return Results.NotFound();

    await using var db = await dbFactory.CreateDbContextAsync(ct);
    var rawEvents = await db.Events
        .Where(e => e.SessionId == sessionId)
        .ToListAsync(ct);

    // In-memory sort (Phase 2 SQLite DateTimeOffset limitation)
    var sorted = rawEvents.OrderBy(e => e.TsUtc).AsEnumerable();

    // Kind filter
    if (!string.IsNullOrEmpty(kind))
    {
        var kindValues = kind.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var parsedKinds = new List<SessionEventKind>();
        foreach (var k in kindValues)
        {
            if (!Enum.TryParse<SessionEventKind>(k, ignoreCase: true, out var parsed))
                return Results.BadRequest(new { error = $"Invalid kind value: '{k}'" });
            parsedKinds.Add(parsed);
        }
        sorted = sorted.Where(e => parsedKinds.Contains(e.Kind));
    }

    var filtered = sorted.ToList();
    var totalCount = filtered.Count;
    var p = page ?? 1;
    var ps = Math.Clamp(pageSize ?? 100, 1, 500);
    var items = filtered
        .Skip((p - 1) * ps)
        .Take(ps)
        .Select(e => e.ToDto())
        .ToList();

    return Results.Json(new { items, totalCount });
});
```

### CLI Client Update
```csharp
// AgentHubApiClient.cs - update GetSessionHistoryAsync
public async Task<(List<SessionEvent> Items, int TotalCount)> GetSessionHistoryAsync(
    string sessionId, int? page = null, int? pageSize = null, string? kind = null, CancellationToken ct = default)
{
    // ... same query string building ...
    var response = await _http.GetAsync(url, ct);
    response.EnsureSuccessStatusCode();
    var result = await response.Content.ReadFromJsonAsync<SessionHistoryResponse>(s_json, ct)
        ?? throw new InvalidOperationException("Failed to deserialize session history response.");
    return (result.Items, result.TotalCount);
}

internal sealed record SessionHistoryResponse(List<SessionEvent> Items, int TotalCount);
```

### Web Client Update
```csharp
// DashboardApiClient.cs - same pattern
public async Task<(List<SessionEvent> Items, int TotalCount)> GetSessionHistoryAsync(
    string sessionId, int? page = null, int? pageSize = null, string? kind = null,
    CancellationToken ct = default)
{
    // ... same query string building ...
    var resp = await http.GetFromJsonAsync<SessionHistoryResponse>(url, s_json, ct);
    return (resp?.Items ?? [], resp?.TotalCount ?? 0);
}

internal sealed record SessionHistoryResponse(List<SessionEvent> Items, int TotalCount);
```

### Expandable Metadata Row in TerminalOutput.razor
```razor
@foreach (var evt in Events)
{
    <div class="terminal-event-row" @onclick="() => ToggleMeta(evt)">
        <pre class="@GetEventClass(evt.Kind)">@evt.Data</pre>
    </div>
    @if (_expandedEvents.Contains(evt) && evt.Meta is { Count: > 0 })
    {
        <div class="terminal-meta-panel">
            @foreach (var kv in evt.Meta)
            {
                <span class="terminal-meta-key">@kv.Key:</span>
                <span class="terminal-meta-value">@kv.Value</span>
            }
        </div>
    }
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Anonymous objects in endpoint | EntityMappers.ToDto() | Phase 5 (this phase) | Typed DTOs for all consumers |
| Bare array response | {items, totalCount} envelope | Phase 2 (sessions), Phase 5 (history) | Consistent pagination contract |
| No kind filter | Comma-separated, case-insensitive | Phase 5 (this phase) | CLI --kind and Web filtering enabled |

## Discretion Recommendations

### DB Auto-Increment Id in DTO
**Recommendation:** Do NOT add Id to the SessionEvent record. The SSE path already stores eventId in the Meta dictionary when emitting events via DurableEventService. The CLI follow mode extracts it from `Meta["eventId"]` (SessionLogsCommand.cs line 63). Adding Id to the DTO would require changing the Contracts record, which is a cross-cutting change. Keep the existing Meta-based approach.

### CLI Metadata Display
**Recommendation:** Show metadata on a second indented line when Meta is non-null and non-empty, using dimmed/grey styling. This matches the existing RenderEvent pattern without requiring a new --verbose flag. Example: `12:34:56 StdOut output text\n         meta: key1=val1 key2=val2`

### Out-of-Range Page Behavior
**Recommendation:** Return empty items array with correct totalCount. This is the most RESTful approach -- the client requested a valid page number that simply has no results. No error needed. The CLI pagination loop handles this naturally (empty batch = stop).

### Max Page Size Cap
**Recommendation:** Cap at 500. Sessions can accumulate thousands of events over hours of agent execution. A 500-event page is ~200KB of JSON at average event size -- well within reasonable HTTP response limits. The default remains 100.

### Client-Side Envelope Changes
**Recommendation:** Both clients must change `GetSessionHistoryAsync` return type from `List<SessionEvent>` to `(List<SessionEvent> Items, int TotalCount)`. All callers must be updated:
- `SessionLogsCommand.LoadHistoryAsync`: destructure tuple, use `.Items` for the list
- `SessionDetail.razor`: destructure tuple in pagination loop

## Open Questions

1. **Does DurableEventService populate Meta with eventId?**
   - What we know: SSE SseItem.EventId is set to DB auto-increment Id (decision 01-02). CLI follow mode reads `Meta["eventId"]` to get Last-Event-ID.
   - What's unclear: Whether the history endpoint needs to inject eventId into Meta, or if it's already stored in MetaJson in the DB.
   - Recommendation: Check DurableEventService.EmitAsync to verify. If eventId is not in MetaJson, the endpoint can inject it from `entity.Id` during ToDto conversion (or add it post-mapping). This is a minor detail that can be resolved during implementation.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit + WebApplicationFactory |
| Config file | tests/AgentHub.Tests/AgentHub.Tests.csproj |
| Quick run command | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SessionHistoryTests" --no-build -v q` |
| Full suite command | `dotnet test tests/AgentHub.Tests --no-build -v q` |

### Phase Requirements -> Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| SESS-05 | History returns SessionEvent DTOs via ToDto() | integration | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SessionHistoryTests.GetSessionHistory_ReturnsSessionEventDtos" -x` | No -- Wave 0 |
| SESS-05 | History pagination returns {items, totalCount} envelope | integration | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SessionHistoryTests.GetSessionHistory_ReturnsPaginatedEnvelope" -x` | No -- Wave 0 |
| SESS-05 | Kind filter returns only matching events | integration | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SessionHistoryTests.GetSessionHistory_WithKindFilter" -x` | No -- Wave 0 |
| SESS-05 | Invalid kind returns 400 | integration | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SessionHistoryTests.GetSessionHistory_InvalidKind_Returns400" -x` | No -- Wave 0 |
| SESS-05 | totalCount reflects filtered count | integration | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SessionHistoryTests.GetSessionHistory_TotalCountReflectsFilter" -x` | No -- Wave 0 |
| SESS-05 | Meta dictionary populated (not null/raw JSON) | integration | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SessionHistoryTests.GetSessionHistory_MetaDeserialized" -x` | No -- Wave 0 |
| CLI-01 | CLI client handles envelope response | integration | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SessionHistoryTests.CliClient_HandlesEnvelope" -x` | No -- Wave 0 |
| WEB-02 | Web client handles envelope response | integration | `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SessionHistoryTests.WebClient_HandlesEnvelope" -x` | No -- Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test tests/AgentHub.Tests --filter "FullyQualifiedName~SessionHistoryTests" --no-build -v q`
- **Per wave merge:** `dotnet test tests/AgentHub.Tests --no-build -v q`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] New test methods in `tests/AgentHub.Tests/SessionHistoryTests.cs` -- update existing `GetSessionHistory_ReturnsEventsOrderedByTimestamp` to verify envelope shape, add new tests for pagination math, kind filtering, 400 on invalid kind, Meta deserialization, totalCount accuracy
- [ ] Seed helper update -- `SeedSessionWithEvents` needs variant that sets MetaJson on some events for Meta deserialization tests

## Sources

### Primary (HIGH confidence)
- Direct code inspection of Program.cs (lines 116-140) -- current endpoint implementation
- Direct code inspection of EntityMappers.cs -- existing ToDto() ready to use
- Direct code inspection of Models.cs -- SessionEvent record and SessionEventKind enum
- Direct code inspection of AgentHubApiClient.cs -- current bare array deserialization
- Direct code inspection of DashboardApiClient.cs -- current bare array deserialization
- Direct code inspection of SessionLogsCommand.cs -- pagination loop and Meta["eventId"] extraction
- Direct code inspection of SessionDetail.razor -- pagination loop with batch.Count == 100
- Direct code inspection of TerminalOutput.razor -- current rendering without metadata
- Direct code inspection of SessionHistoryTests.cs -- existing test infrastructure

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH -- all libraries already in use, no new dependencies
- Architecture: HIGH -- directly replicating established Phase 2 patterns
- Pitfalls: HIGH -- identified from direct code inspection of all consumer sites

**Research date:** 2026-03-09
**Valid until:** 2026-04-09 (stable -- no external dependencies changing)
