---
status: resolved
trigger: "SQLite NotSupportedException DateTimeOffset in ORDER BY in SessionMonitorService.RunOnceAsync line 78"
created: 2026-03-10T00:00:00Z
updated: 2026-03-10T00:00:00Z
---

## Current Focus

hypothesis: CONFIRMED - OrderByDescending on DateTimeOffset column TsUtc cannot be translated by SQLite EF Core provider
test: Read source at line 78
expecting: LINQ OrderBy on DateTimeOffset column
next_action: Return diagnosis

## Symptoms

expected: SessionMonitorService background loop runs without error
actual: System.NotSupportedException: SQLite does not support expressions of type 'DateTimeOffset' in ORDER BY clauses
errors: System.NotSupportedException at RunOnceAsync line 78
reproduction: Any monitoring cycle with running sessions triggers it
started: Present since code was written

## Eliminated

(none needed - root cause found on first pass)

## Evidence

- timestamp: 2026-03-10
  checked: SessionMonitorService.cs line 78-82
  found: `.OrderByDescending(e => e.TsUtc)` where TsUtc is DateTimeOffset
  implication: SQLite EF Core provider cannot translate DateTimeOffset in ORDER BY

## Resolution

root_cause: Line 80 of SessionMonitorService.cs uses `.OrderByDescending(e => e.TsUtc)` where `TsUtc` is `DateTimeOffset`. The SQLite EF Core provider does not support translating DateTimeOffset expressions in ORDER BY clauses.
fix: Use AsEnumerable() to pull matching events into memory before ordering, or convert to string for server-side ordering.
verification: pending
files_changed:
- src/AgentHub.Orchestration/Monitoring/SessionMonitorService.cs
