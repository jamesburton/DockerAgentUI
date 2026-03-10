using System.CommandLine;
using Spectre.Console;
using AgentHub.Cli.Api;
using AgentHub.Cli.Config;
using AgentHub.Cli.Output;
using AgentHub.Cli.Commands;
using AgentHub.Cli.Commands.Session;
using AgentHub.Cli.Commands.Host;
using AgentHub.Cli.Commands.Worktree;
using AgentHub.Cli.Notifications;
using AgentHub.Contracts;

var config = CliConfig.Load();
var notificationService = new NotificationService();

// -- Global options --
var jsonOption = new Option<bool>("--json") { Description = "Output in JSON format", Recursive = true };
var noColorOption = new Option<bool>("--no-color") { Description = "Disable colored output", Recursive = true };
var verboseOption = new Option<bool>("-v", "--verbose") { Description = "Verbose output", Recursive = true };
var quietOption = new Option<bool>("-q", "--quiet") { Description = "Suppress all non-essential output", Recursive = true };
var serverOption = new Option<string>("--server") { Description = "Override server URL", Recursive = true };

var rootCommand = new RootCommand("AgentHub CLI - manage agent sessions across your fleet");
rootCommand.Add(jsonOption);
rootCommand.Add(noColorOption);
rootCommand.Add(verboseOption);
rootCommand.Add(quietOption);
rootCommand.Add(serverOption);

// -- Service factories --
HttpClient ResolveHttpClient(ParseResult pr)
{
    var serverUrl = pr.GetValue(serverOption) ?? config.ServerUrl;
    return new HttpClient { BaseAddress = new Uri(serverUrl) };
}

AgentHubApiClient ResolveClient(ParseResult pr)
    => new(ResolveHttpClient(pr));

SseStreamReader ResolveSseReader(ParseResult pr)
    => new(ResolveHttpClient(pr));

IOutputFormatter ResolveFormatter(ParseResult pr)
    => pr.GetValue(jsonOption) ? new JsonFormatter() : new TableFormatter();

// ============================================================
// SESSION group
// ============================================================
var sessionCommand = new Command("session", "Manage agent sessions");

// -- session start --
{
    var agentArg = new Argument<string>("agent") { Description = "Agent type (e.g. claude)" };
    var promptArg = new Argument<string>("prompt") { Description = "Prompt or task description" };
    var hostOpt = new Option<string>("--host") { Description = "Target host ID for placement" };
    var detachOpt = new Option<bool>("-d", "--detach") { Description = "Run in background", DefaultValueFactory = _ => false };
    var ffOpt = new Option<bool>("--fire-and-forget") { Description = "Fire and forget mode", DefaultValueFactory = _ => false };
    var keepBranchOpt = new Option<bool>("--keep-branch") { Description = "Preserve worktree branch after session ends (for cherry-picking)", DefaultValueFactory = _ => false };

    var cmd = new Command("start", "Start a new agent session");
    cmd.Add(agentArg);
    cmd.Add(promptArg);
    cmd.Add(hostOpt);
    cmd.Add(detachOpt);
    cmd.Add(ffOpt);
    cmd.Add(keepBranchOpt);

    cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
    {
        var client = ResolveClient(pr);
        var formatter = ResolveFormatter(pr);

        var req = new StartSessionRequest(
            pr.GetValue(agentArg)!,
            new SessionRequirements(TargetHostId: pr.GetValue(hostOpt)),
            Prompt: pr.GetValue(promptArg),
            IsFireAndForget: pr.GetValue(ffOpt),
            KeepBranch: pr.GetValue(keepBranchOpt));

        var sessionId = await client.StartSessionAsync(req, ct);
        formatter.WriteSuccess($"Session {sessionId} started");

        if (!pr.GetValue(detachOpt))
            Console.WriteLine($"Attached to session {sessionId}. Use Ctrl+C to detach.");

        return 0;
    });

    sessionCommand.Add(cmd);
}

// -- session list --
{
    var stateOpt = new Option<string>("--state") { Description = "Filter by session state" };
    var takeOpt = new Option<int>("--take") { Description = "Number of sessions to return", DefaultValueFactory = _ => 20 };

    var cmd = new Command("list", "List sessions");
    cmd.Add(stateOpt);
    cmd.Add(takeOpt);

    cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
    {
        var client = ResolveClient(pr);
        var formatter = ResolveFormatter(pr);
        var verbose = pr.GetValue(verboseOption);

        var (items, _) = await client.GetSessionsAsync(
            take: pr.GetValue(takeOpt), state: pr.GetValue(stateOpt), ct: ct);

        formatter.WriteTable(items, (table, s) =>
        {
            var id = verbose ? s.SessionId : Truncate(s.SessionId, 8);
            var st = FormatState(s.State);
            var host = s.Node ?? "(auto)";
            var created = s.CreatedUtc.ToLocalTime().ToString("HH:mm:ss");
            var dur = (DateTimeOffset.UtcNow - s.CreatedUtc).ToString(@"hh\:mm\:ss");

            if (verbose)
                table.AddRow(id, st, s.Backend, host, s.Backend, created, dur);
            else
                table.AddRow(id, st, s.Backend, host, created, dur);
        },
        verbose
            ? ["ID", "State", "Agent", "Host", "Backend", "Created", "Duration"]
            : ["ID", "State", "Agent", "Host", "Created", "Duration"]);

        return 0;
    });

    sessionCommand.Add(cmd);
}

// -- session stop --
{
    var idArg = new Argument<string>("sessionId") { Description = "Session ID to stop" };
    var forceOpt = new Option<bool>("-f", "--force") { Description = "Force stop", DefaultValueFactory = _ => false };

    var cmd = new Command("stop", "Stop a running session");
    cmd.Add(idArg);
    cmd.Add(forceOpt);

    cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
    {
        var client = ResolveClient(pr);
        var formatter = ResolveFormatter(pr);
        var sessionId = pr.GetValue(idArg)!;

        await client.StopSessionAsync(sessionId, pr.GetValue(forceOpt), ct);
        formatter.WriteSuccess($"Session {sessionId} stopped");
        return 0;
    });

    sessionCommand.Add(cmd);
}

// -- session watch --
{
    var idArg = new Argument<string>("sessionId") { Description = "Session ID to watch" };

    var cmd = new Command("watch", "Live session dashboard with streaming events");
    cmd.Add(idArg);

    cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
    {
        var client = ResolveClient(pr);
        var sseReader = ResolveSseReader(pr);
        var formatter = ResolveFormatter(pr);
        var isJson = pr.GetValue(jsonOption);
        var approvalHandler = isJson ? null : new ApprovalPromptHandler(client);

        return await SessionWatchCommand.ExecuteAsync(
            pr.GetValue(idArg)!, isJson, client, sseReader, formatter, approvalHandler, ct);
    });

    sessionCommand.Add(cmd);
}

// -- session input --
{
    var idArg = new Argument<string>("id") { Description = "Session ID to send input to" };
    var textArg = new Argument<string?>("text") { Description = "Text to send (reads from stdin if omitted)", DefaultValueFactory = _ => null };

    var cmd = new Command("input", "Send input to a running session");
    cmd.Add(idArg);
    cmd.Add(textArg);

    cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
    {
        var client = ResolveClient(pr);
        var formatter = ResolveFormatter(pr);
        var sessionId = pr.GetValue(idArg)!;
        var text = pr.GetValue(textArg);

        if (string.IsNullOrEmpty(text))
        {
            text = await Console.In.ReadToEndAsync(ct);
        }

        try
        {
            await client.SendInputAsync(sessionId, text!, ct);
            formatter.WriteSuccess($"Input sent to session {sessionId}.");
            return 0;
        }
        catch (HttpRequestException ex)
        {
            formatter.WriteError($"Failed to send input: {ex.Message}");
            return 1;
        }
    });

    sessionCommand.Add(cmd);
}

// -- session logs --
{
    var idArg = new Argument<string>("sessionId") { Description = "Session ID to view logs for" };
    var allOpt = new Option<bool>("--all") { Description = "Show all log history", DefaultValueFactory = _ => false };
    var tailOpt = new Option<int>("--tail") { Description = $"Number of recent lines to show (default: {config.LogTailDefault})", DefaultValueFactory = _ => config.LogTailDefault };
    var followOpt = new Option<bool>("-f", "--follow") { Description = "Follow live output", DefaultValueFactory = _ => false };
    var kindOpt = new Option<string>("--kind") { Description = "Filter by event kind (e.g. StdOut, StdErr, StateChanged)" };

    var cmd = new Command("logs", "View session output history");
    cmd.Add(idArg);
    cmd.Add(allOpt);
    cmd.Add(tailOpt);
    cmd.Add(followOpt);
    cmd.Add(kindOpt);

    cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
    {
        var client = ResolveClient(pr);
        var sseReader = ResolveSseReader(pr);
        var formatter = ResolveFormatter(pr);
        var isJson = pr.GetValue(jsonOption);

        return await SessionLogsCommand.ExecuteAsync(
            pr.GetValue(idArg)!,
            pr.GetValue(allOpt),
            pr.GetValue(tailOpt),
            pr.GetValue(followOpt),
            pr.GetValue(kindOpt),
            isJson,
            client, sseReader, formatter, ct);
    });

    sessionCommand.Add(cmd);
}

// -- session diff --
{
    var idArg = new Argument<string>("sessionId") { Description = "Session ID to view diff for" };
    var detailedOpt = new Option<bool>("--detailed") { Description = "Show full table with per-file stats", DefaultValueFactory = _ => false };

    var cmd = new Command("diff", "View diff stats for a completed worktree session");
    cmd.Add(idArg);
    cmd.Add(detailedOpt);

    cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
    {
        var client = ResolveClient(pr);
        var formatter = ResolveFormatter(pr);

        return await SessionDiffCommand.ExecuteAsync(
            pr.GetValue(idArg)!,
            pr.GetValue(detailedOpt),
            client, formatter, ct);
    });

    sessionCommand.Add(cmd);
}

// ============================================================
// WORKTREE group
// ============================================================
var worktreeCommand = new Command("worktree", "Manage git worktrees");

// -- worktree cleanup --
{
    var hostOpt = new Option<string>("--host") { Description = "Host ID to clean up orphaned worktrees on" };
    hostOpt.Required = true;

    var cmd = new Command("cleanup", "Remove orphaned worktrees from a host");
    cmd.Add(hostOpt);

    cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
    {
        var client = ResolveClient(pr);
        var formatter = ResolveFormatter(pr);

        return await WorktreeCleanupCommand.ExecuteAsync(
            pr.GetValue(hostOpt)!,
            client, formatter, ct);
    });

    worktreeCommand.Add(cmd);
}

// ============================================================
// HOST group
// ============================================================
var hostCommand = new Command("host", "Manage fleet hosts");

// -- host list --
{
    var cmd = new Command("list", "List registered hosts");

    cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
    {
        var client = ResolveClient(pr);
        var formatter = ResolveFormatter(pr);
        var verbose = pr.GetValue(verboseOption);
        var hosts = await client.GetHostsAsync(ct);

        formatter.WriteTable(hosts, (table, h) =>
        {
            var enabled = h.Enabled ? "[green]Yes[/]" : "[red]No[/]";
            var ssh = h.AllowSsh ? "Yes" : "No";

            if (verbose)
            {
                var addr = h.Address ?? "--";
                var labels = h.Labels is { Count: > 0 }
                    ? string.Join(", ", h.Labels.Select(kv => $"{kv.Key}={kv.Value}"))
                    : "--";
                table.AddRow(h.HostId, h.DisplayName, h.Os, h.Backend, enabled, ssh, addr, labels);
            }
            else
            {
                table.AddRow(h.HostId, h.DisplayName, h.Os, h.Backend, enabled, ssh);
            }
        },
        verbose
            ? ["ID", "Name", "OS", "Backend", "Enabled", "SSH", "Address", "Labels"]
            : ["ID", "Name", "OS", "Backend", "Enabled", "SSH"]);

        return 0;
    });

    hostCommand.Add(cmd);
}

// -- host status --
{
    var watchOpt = new Option<bool>("--watch") { Description = "Live refresh of host metrics", DefaultValueFactory = _ => false };
    var cmd = new Command("status", "Show host resource status (CPU, memory)");
    cmd.Add(watchOpt);

    cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
    {
        var client = ResolveClient(pr);
        var formatter = ResolveFormatter(pr);

        if (pr.GetValue(watchOpt))
        {
            return await HostStatusCommand.ExecuteWatchAsync(client, config, formatter, ct);
        }

        var hosts = await client.GetHostsAsync(ct);

        formatter.WriteTable(hosts, (table, h) =>
        {
            var cpu = h.CpuPercent.HasValue ? FormatCpu(h.CpuPercent.Value) : "--";
            var mem = h.MemUsedMb.HasValue && h.MemTotalMb.HasValue
                ? FormatMem(h.MemUsedMb.Value, h.MemTotalMb.Value) : "--";
            table.AddRow(h.DisplayName, h.Os, cpu, mem, "--");
        },
        "Name", "OS", "CPU%", "Memory", "Sessions");

        return 0;
    });

    hostCommand.Add(cmd);
}

// ============================================================
// CONFIG group
// ============================================================
var configCommand = new Command("config", "CLI configuration");

// -- config show --
{
    var cmd = new Command("show", "Show current CLI configuration");

    cmd.SetAction((ParseResult pr) =>
    {
        var formatter = ResolveFormatter(pr);
        formatter.WriteObject(new
        {
            config.ServerUrl,
            config.DefaultHost,
            config.DefaultAgent,
            config.WatchRefreshMs,
            config.LogTailDefault,
            ConfigPath = CliConfig.ConfigPath
        });
        return 0;
    });

    configCommand.Add(cmd);
}

// ============================================================
// TOP-LEVEL COMMANDS
// ============================================================

// -- watch (fleet overview) --
{
    var cmd = new Command("watch", "Live fleet overview of all running sessions");

    cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
    {
        var client = ResolveClient(pr);
        var sseReader = ResolveSseReader(pr);
        var formatter = ResolveFormatter(pr);
        var isJson = pr.GetValue(jsonOption);

        return await WatchCommand.ExecuteAsync(isJson, client, sseReader, config, formatter, ct);
    });

    rootCommand.Add(cmd);
}

// -- listen (notification stream) --
{
    var cmd = new Command("listen", "Stream notable fleet events (completions, failures, approvals)");

    cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
    {
        var sseReader = ResolveSseReader(pr);
        var isJson = pr.GetValue(jsonOption);

        return await ListenCommand.ExecuteAsync(isJson, sseReader, notificationService, ct);
    });

    rootCommand.Add(cmd);
}

// -- run (alias for session start) --
{
    var agentArg = new Argument<string>("agent") { Description = "Agent type (e.g. claude)" };
    var promptArg = new Argument<string>("prompt") { Description = "Prompt or task description" };
    var hostOpt = new Option<string>("--host") { Description = "Target host ID" };
    var detachOpt = new Option<bool>("-d", "--detach") { Description = "Run in background", DefaultValueFactory = _ => false };

    var cmd = new Command("run", "Start a new agent session (shortcut for 'session start')");
    cmd.Add(agentArg);
    cmd.Add(promptArg);
    cmd.Add(hostOpt);
    cmd.Add(detachOpt);

    cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
    {
        var client = ResolveClient(pr);
        var formatter = ResolveFormatter(pr);

        var req = new StartSessionRequest(
            pr.GetValue(agentArg)!,
            new SessionRequirements(TargetHostId: pr.GetValue(hostOpt)),
            Prompt: pr.GetValue(promptArg));

        var sessionId = await client.StartSessionAsync(req, ct);
        formatter.WriteSuccess($"Session {sessionId} started");

        if (!pr.GetValue(detachOpt))
            Console.WriteLine($"Attached to session {sessionId}. Use Ctrl+C to detach.");

        return 0;
    });

    rootCommand.Add(cmd);
}

// -- ls (alias for session list) --
{
    var stateOpt = new Option<string>("--state") { Description = "Filter by state" };
    var takeOpt = new Option<int>("--take") { Description = "Number of sessions", DefaultValueFactory = _ => 20 };

    var cmd = new Command("ls", "List sessions (shortcut for 'session list')");
    cmd.Add(stateOpt);
    cmd.Add(takeOpt);

    cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
    {
        var client = ResolveClient(pr);
        var formatter = ResolveFormatter(pr);

        var (items, _) = await client.GetSessionsAsync(
            take: pr.GetValue(takeOpt), state: pr.GetValue(stateOpt), ct: ct);

        formatter.WriteTable(items, (table, s) =>
        {
            var id = Truncate(s.SessionId, 8);
            var host = s.Node ?? "(auto)";
            var created = s.CreatedUtc.ToLocalTime().ToString("HH:mm:ss");
            var dur = (DateTimeOffset.UtcNow - s.CreatedUtc).ToString(@"hh\:mm\:ss");
            table.AddRow(id, s.State.ToString(), s.Backend, host, created, dur);
        },
        "ID", "State", "Agent", "Host", "Created", "Duration");

        return 0;
    });

    rootCommand.Add(cmd);
}

// ============================================================
// Add command groups
// ============================================================
rootCommand.Add(sessionCommand);
rootCommand.Add(hostCommand);
rootCommand.Add(worktreeCommand);
rootCommand.Add(configCommand);

// ============================================================
// Execute
// ============================================================
var cliConfiguration = new CommandLineConfiguration(rootCommand)
{
    EnableDefaultExceptionHandler = false
};

// Show pending notifications from previous `ah listen` sessions
try
{
    notificationService.ShowPendingSummary(new TableFormatter());
}
catch
{
    // Don't let notification display errors block command execution
}

try
{
    return await cliConfiguration.InvokeAsync(args);
}
catch (HttpRequestException ex)
{
    var pr = cliConfiguration.Parse(args);
    ResolveFormatter(pr).WriteError($"API error: {ex.Message}");
    return 1;
}
catch (OperationCanceledException)
{
    return 2;
}

// ============================================================
// Helpers
// ============================================================
static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

static string FormatState(SessionState state) => state switch
{
    SessionState.Running => "[green]Running[/]",
    SessionState.Failed => "[red]Failed[/]",
    SessionState.Stopped => "[grey]Stopped[/]",
    SessionState.Pending => "[yellow]Pending[/]",
    _ => state.ToString()
};

static string FormatCpu(double cpuPercent)
{
    var v = $"{cpuPercent:F1}%";
    return cpuPercent switch { > 80 => $"[red]{v}[/]", > 60 => $"[yellow]{v}[/]", _ => $"[green]{v}[/]" };
}

static string FormatMem(long usedMb, long totalMb)
{
    var pct = totalMb > 0 ? (double)usedMb / totalMb * 100 : 0;
    var v = $"{usedMb}/{totalMb} MB";
    return pct switch { > 90 => $"[red]{v}[/]", > 70 => $"[yellow]{v}[/]", _ => $"[green]{v}[/]" };
}
