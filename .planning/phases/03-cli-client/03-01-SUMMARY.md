---
phase: 03-cli-client
plan: 01
subsystem: cli
tags: [system-commandline, spectre-console, http-client, cli, dotnet]

requires:
  - phase: 02-session-orchestration
    provides: REST API endpoints for sessions, hosts, approvals, events
provides:
  - AgentHub.Cli console project with System.CommandLine and Spectre.Console
  - AgentHubApiClient typed HTTP client for all REST endpoints
  - IOutputFormatter abstraction with TableFormatter and JsonFormatter
  - CliConfig for ~/.agenthub/config.json settings
  - Full command tree: session start/list/stop, host list/status, config show
  - Top-level shortcuts: run, ls
  - HostRecord extended with CpuPercent, MemUsedMb, MemTotalMb
affects: [03-cli-client, 04-web-dashboard]

tech-stack:
  added: [System.CommandLine 2.0.0-beta5, Spectre.Console 0.50.0]
  patterns: [inline command builders with ParseResult closures, recursive global options, IOutputFormatter table/json dual output]

key-files:
  created:
    - src/AgentHub.Cli/AgentHub.Cli.csproj
    - src/AgentHub.Cli/Program.cs
    - src/AgentHub.Cli/Api/AgentHubApiClient.cs
    - src/AgentHub.Cli/Output/IOutputFormatter.cs
    - src/AgentHub.Cli/Output/TableFormatter.cs
    - src/AgentHub.Cli/Output/JsonFormatter.cs
    - src/AgentHub.Cli/Config/CliConfig.cs
    - tests/AgentHub.Tests/ApiClientTests.cs
    - tests/AgentHub.Tests/OutputFormatterTests.cs
  modified:
    - src/AgentHub.Contracts/Models.cs
    - AgentSafeEnv.sln
    - tests/AgentHub.Tests/AgentHub.Tests.csproj

key-decisions:
  - "System.CommandLine 2.0.0-beta5 uses Command/Option/Argument (not CliCommand) with Recursive=true for global option propagation"
  - "Inline command definition in Program.cs with ParseResult closures instead of separate command class files for simplicity"
  - "SetAction returns int for exit code control (0=success, 1=error, 2=timeout)"
  - "AgentHubApiClient takes HttpClient directly, formatter resolved per-command from ParseResult"

patterns-established:
  - "IOutputFormatter: dual table/JSON output -- WriteTable for collections, WriteObject for single items, WriteError/WriteSuccess for status"
  - "ParseResult closures: global options captured via recursive propagation and resolved inside SetAction lambdas"
  - "CliConfig: file-based config at ~/.agenthub/config.json with safe Load() fallback to defaults"

requirements-completed: [CLI-01, CLI-02, MON-02]

duration: 11min
completed: 2026-03-08
---

# Phase 3 Plan 1: CLI Core Commands Summary

**Full CRUD CLI tool with session start/list/stop, host list/status, config show, run/ls shortcuts, --json mode, and 11 unit tests using System.CommandLine beta5 and Spectre.Console**

## Performance

- **Duration:** 11 min
- **Started:** 2026-03-08T17:04:49Z
- **Completed:** 2026-03-08T17:16:42Z
- **Tasks:** 2
- **Files modified:** 12

## Accomplishments
- AgentHub.Cli console project integrated into solution with System.CommandLine, Spectre.Console, and AgentHub.Contracts reference
- AgentHubApiClient with typed methods for all REST endpoints (sessions CRUD, hosts, history, approvals)
- IOutputFormatter abstraction with TableFormatter (Spectre.Console rounded tables) and JsonFormatter (raw JSON to stdout)
- Full command tree: session (start/list/stop), host (list/status), config (show), plus run and ls shortcuts
- Global options --json, --no-color, -v, -q, --server propagated to all subcommands via Recursive=true
- HostRecord extended with CpuPercent, MemUsedMb, MemTotalMb nullable fields for resource monitoring
- 11 unit tests passing: 6 for ApiClient (mock HTTP handler), 5 for OutputFormatter (console capture)

## Task Commits

Each task was committed atomically:

1. **Task 1: Create CLI project with API client, output formatters, and config** - `abcbd14` (feat)
2. **Task 2: Build command tree with all CRUD commands and shortcuts** - `aab2766` (feat)

## Files Created/Modified
- `src/AgentHub.Cli/AgentHub.Cli.csproj` - Console project with System.CommandLine, Spectre.Console packages
- `src/AgentHub.Cli/Program.cs` - Full command tree with 6 commands, 2 shortcuts, global options
- `src/AgentHub.Cli/Api/AgentHubApiClient.cs` - Typed HTTP client for all REST endpoints
- `src/AgentHub.Cli/Output/IOutputFormatter.cs` - Output abstraction interface
- `src/AgentHub.Cli/Output/TableFormatter.cs` - Spectre.Console table rendering
- `src/AgentHub.Cli/Output/JsonFormatter.cs` - JSON serialization to stdout/stderr
- `src/AgentHub.Cli/Config/CliConfig.cs` - Config file reader for ~/.agenthub/config.json
- `src/AgentHub.Contracts/Models.cs` - HostRecord extended with resource metric fields
- `AgentSafeEnv.sln` - CLI project added to solution
- `tests/AgentHub.Tests/AgentHub.Tests.csproj` - CLI project reference added
- `tests/AgentHub.Tests/ApiClientTests.cs` - 6 API client tests with mock handler
- `tests/AgentHub.Tests/OutputFormatterTests.cs` - 5 output formatter tests

## Decisions Made
- System.CommandLine 2.0.0-beta5 API uses `Command`/`Option<T>`/`Argument<T>` (not the `CliCommand` prefix from earlier betas); `Recursive=true` enables global option propagation to subcommands
- Commands defined inline in Program.cs with ParseResult closures rather than separate static factory classes; cleaner with the beta5 API and avoids unnecessary abstraction for 6 commands
- `SetAction(Func<ParseResult, CancellationToken, Task<int>>)` overload returns exit code directly (0=success, 1=error, 2=timeout)
- AgentHubApiClient resolves fresh from ParseResult per-command to pick up --server override; formatter likewise resolved per-command for --json

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] System.CommandLine API adaptation from CliCommand to Command**
- **Found during:** Task 2 (command tree building)
- **Issue:** Plan specified CliRootCommand/CliCommand/CliOption/CliConfiguration API which does not exist in System.CommandLine 2.0.0-beta5; actual API uses RootCommand/Command/Option/CommandLineConfiguration
- **Fix:** Adapted all command code to use the actual beta5 API surface; used Recursive=true for global option propagation instead of Cli-prefixed types
- **Files modified:** src/AgentHub.Cli/Program.cs
- **Verification:** All commands build and --help output shows correct tree

**2. [Rule 1 - Bug] Separate command files removed in favor of inline definitions**
- **Found during:** Task 2 (command tree building)
- **Issue:** Separate command files (SessionStartCommand.cs, etc.) referenced non-existent CliCommand type and would require proxy patterns for service resolution
- **Fix:** Defined all commands inline in Program.cs using ParseResult closures; simpler and works directly with the actual API
- **Files modified:** Removed 6 unused command files, all logic in Program.cs
- **Verification:** All 6 commands + 2 shortcuts work correctly

---

**Total deviations:** 2 auto-fixed (1 blocking, 1 bug)
**Impact on plan:** Both deviations necessary to work with actual System.CommandLine API. All must-have truths satisfied. No scope creep.

## Issues Encountered
- Spectre.Console Table.AddRow is an extension method requiring `using Spectre.Console;` -- initial build failure resolved by adding the using directive
- MAUI project has pre-existing build errors unrelated to CLI changes (out of scope)

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- CLI project ready for Plan 02 (streaming, live watch, notifications)
- AgentHubApiClient provides foundation for SSE stream integration
- IOutputFormatter abstraction supports future LiveDisplay patterns
- All REST endpoints covered; SSE streaming methods deferred to Plan 02 per spec

---
*Phase: 03-cli-client*
*Completed: 2026-03-08*
