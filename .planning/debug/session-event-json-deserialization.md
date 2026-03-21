---
status: awaiting_human_verify
trigger: "Error loading session: The JSON value could not be converted to AgentHub.Contracts.SessionEvent. Path: $.items[0].kind | LineNumber: 0 | BytePositionInLine: 83."
created: 2026-03-10T00:00:00Z
updated: 2026-03-10T00:00:00Z
---

## Current Focus

hypothesis: CONFIRMED - Server serializes enums as strings (JsonStringEnumConverter in HistoryJson.Options), client deserializes without JsonStringEnumConverter (expects integers)
test: Compare JsonSerializerOptions between server history endpoint and DashboardApiClient
expecting: Mismatch confirmed
next_action: Add JsonStringEnumConverter to DashboardApiClient s_json options

## Symptoms

expected: Session detail page loads and displays session events correctly
actual: UI shows "Error loading session" with JSON deserialization error for SessionEvent.kind field
errors: "The JSON value could not be converted to AgentHub.Contracts.SessionEvent. Path: $.items[0].kind | LineNumber: 0 | BytePositionInLine: 83."
reproduction: Open any session detail page in the Web UI
started: Unknown - may be related to recent changes or new event types

## Eliminated

## Evidence

- timestamp: 2026-03-10T00:01:00Z
  checked: Server-side history endpoint JSON options (Program.cs line 436-441)
  found: HistoryJson.Options includes JsonStringEnumConverter - enums serialized as strings
  implication: API sends {"kind":"StdOut",...} not {"kind":1,...}

- timestamp: 2026-03-10T00:01:30Z
  checked: Client-side DashboardApiClient JSON options (DashboardApiClient.cs line 10)
  found: s_json = new(JsonSerializerDefaults.Web) has NO JsonStringEnumConverter
  implication: Client expects integer enum values, receives strings, deserialization fails

## Resolution

root_cause: DashboardApiClient.s_json and SseStreamService.s_json (and CLI equivalents) lack JsonStringEnumConverter. The server history endpoint serializes SessionEventKind as strings (via HistoryJson.Options with JsonStringEnumConverter), but all clients use default JsonSerializerDefaults.Web which expects numeric enum values. When the client receives {"kind":"StdOut",...} it cannot parse the string "StdOut" into the SessionEventKind enum.
fix: Added JsonStringEnumConverter to all four client-side JsonSerializerOptions instances
verification: All three affected projects build successfully (0 errors)
files_changed:
  - src/AgentHub.Web/Services/DashboardApiClient.cs
  - src/AgentHub.Web/Services/SseStreamService.cs
  - src/AgentHub.Cli/Api/AgentHubApiClient.cs
  - src/AgentHub.Cli/Api/SseStreamReader.cs
