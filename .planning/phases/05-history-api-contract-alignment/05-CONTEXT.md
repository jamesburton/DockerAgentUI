# Phase 5: History API Contract Alignment - Context

**Gathered:** 2026-03-09
**Status:** Ready for planning

<domain>
## Phase Boundary

Fix the `/api/sessions/{id}/history` endpoint to return correctly shaped `SessionEvent` DTOs via `EntityMappers.ToDto()` with working pagination (`page`, `pageSize`) and `kind` filtering. Both CLI (`ah session logs`) and Web (SessionDetail history replay) clients are already built expecting this contract but the endpoint currently returns anonymous objects with no pagination support. This is a gap closure phase — align the API with its consumers.

</domain>

<decisions>
## Implementation Decisions

### Response Shape & Metadata
- Use existing `EntityMappers.ToDto()` to convert `SessionEventEntity` to `SessionEvent` DTO — no anonymous objects
- Kind serialized as string name (e.g. "StdOut", "StdErr") — matches existing camelCase JSON serialization and CLI color-coding
- Meta dictionary properly deserialized from MetaJson — clients receive `Dictionary<string, string>?` not raw JSON string
- Web session detail: event metadata displayed via expandable row (click event line to see meta key-value pairs)
- CLI metadata display: Claude's discretion based on existing output patterns

### Pagination Behavior
- Response wrapped in envelope: `{items: [...], totalCount: N}` — consistent with existing `GET /api/sessions` pattern (Phase 2 decision)
- Default page size: 100 events (matches Web client's existing batch size)
- Out-of-range page handling: Claude's discretion (pick most RESTful approach)
- Max page size cap: Claude's discretion (evaluate based on SQLite performance and session size)
- In-memory sorting maintained (Phase 2 decision — SQLite DateTimeOffset ORDER BY limitation)

### Kind Filter Semantics
- Comma-separated multi-value filter supported: `?kind=StdOut,StdErr`
- Case-insensitive parsing: accept "stdout", "STDOUT", "StdOut" — use `Enum.TryParse` with `ignoreCase: true`
- Invalid kind values return 400 Bad Request with error message
- totalCount reflects filtered count (after kind filter applied), not total unfiltered events

### Integration Test Scope
- Full coverage: response shape, pagination math, kind filtering, totalCount accuracy
- Update existing `SessionHistoryTests.cs` in place (don't create separate test class)
- Update client tests: verify both CLI `AgentHubApiClient` and Web `DashboardApiClient` correctly handle the `{items, totalCount}` envelope response

### Claude's Discretion
- Whether to include DB auto-increment Id in the DTO response (evaluate based on SSE replay patterns and client needs)
- CLI metadata display approach (inline, separate line, or verbose-only)
- Out-of-range page behavior (empty result vs error)
- Max page size cap value
- Client-side changes needed to handle envelope response (both CLI and Web API clients currently expect bare arrays)

</decisions>

<specifics>
## Specific Ideas

- The envelope response `{items, totalCount}` matches the existing `GET /api/sessions` pattern established in Phase 2 (decision 02-04)
- CLI `ah session logs` already has `--all` (paginate through everything), `--tail N`, `--kind` filter, and `--follow` — these all depend on correct API pagination
- Web SessionDetail loads history in page=1, pageSize=100 batches — needs to handle envelope instead of bare array
- Expandable row for metadata in TerminalOutput component — click to reveal meta key-value pairs below the event line

</specifics>

<code_context>
## Existing Code Insights

### Reusable Assets
- **EntityMappers.ToDto()** (Orchestration/Data/EntityMappers.cs): Full SessionEventEntity→SessionEvent conversion with MetaJson parsing — ready to use, currently unused by the endpoint
- **SessionEvent record** (Contracts/Models.cs): Correct DTO with Kind as SessionEventKind enum and Meta as Dictionary<string, string>?
- **AgentHubApiClient.GetSessionHistoryAsync** (Cli/Api/AgentHubApiClient.cs): Already sends page, pageSize, kind query parameters — needs update for envelope response
- **DashboardApiClient.GetSessionHistoryAsync** (Web/Services/DashboardApiClient.cs): Already sends page, pageSize, kind query parameters — needs update for envelope response
- **SessionHistoryTests.cs** (Tests): Existing test infrastructure with session/event seeding helpers

### Established Patterns
- In-memory sorting for paginated queries (Phase 2 — SQLite DateTimeOffset limitation)
- GET /api/sessions returns {items, totalCount} envelope (Phase 2 decision 02-04)
- Dual DbContext registration (Pool + Factory) for scoped DI and singleton services
- Enums stored as strings in SQLite for readability
- JsonSerializerDefaults.Web (camelCase) throughout

### Integration Points
- **Program.cs:116-140**: The history endpoint implementation — currently returns anonymous objects, needs full rewrite
- **SessionLogsCommand.cs**: CLI consumer — LoadHistoryAsync paginates with batch detection (batch.Count < pageSize)
- **SessionDetail.razor:117-127**: Web consumer — paginates with batch detection (batch.Count == 100)
- **TerminalOutput.razor**: Web rendering component — needs expandable row support for Meta display

</code_context>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 05-history-api-contract-alignment*
*Context gathered: 2026-03-09*
