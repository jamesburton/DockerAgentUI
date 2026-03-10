using System.Text.Json;
using AgentHub.Cli.Api;
using AgentHub.Cli.Notifications;
using AgentHub.Cli.Output;
using AgentHub.Contracts;
using Spectre.Console;

namespace AgentHub.Cli.Commands.Session;

/// <summary>
/// Implements `ah session watch &lt;id&gt;` - live session dashboard with color-coded events.
/// </summary>
public static class SessionWatchCommand
{
    private const int MaxBufferLines = 500;
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    // Rapid-fire detection
    private static readonly Queue<DateTimeOffset> s_recentInputs = new();
    private const int RapidFireThreshold = 3;
    private static readonly TimeSpan RapidFireWindow = TimeSpan.FromSeconds(10);

    public static async Task<int> ExecuteAsync(
        string sessionId,
        bool jsonMode,
        AgentHubApiClient apiClient,
        SseStreamReader sseReader,
        IOutputFormatter formatter,
        ApprovalPromptHandler? approvalHandler,
        CancellationToken ct,
        bool enableInputHotkey = true)
    {
        // Fetch initial session info
        var session = await apiClient.GetSessionAsync(sessionId, ct);
        if (session is null)
        {
            formatter.WriteError($"Session '{sessionId}' not found.");
            return 1;
        }

        // Set up Ctrl+C handling - detach without stopping session
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        if (jsonMode)
        {
            return await RunJsonModeAsync(sessionId, sseReader, cts.Token);
        }

        return await RunLiveModeAsync(sessionId, session, apiClient, sseReader, approvalHandler, cts.Token);
    }

    private static async Task<int> RunJsonModeAsync(
        string sessionId, SseStreamReader sseReader, CancellationToken ct)
    {
        try
        {
            await foreach (var (_, evt) in sseReader.StreamSessionEventsAsync(sessionId, ct: ct))
            {
                Console.WriteLine(JsonSerializer.Serialize(evt, s_json));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Clean detach
        }

        Console.WriteLine($"Detached from session {sessionId}. Session continues running.");
        return 0;
    }

    private static async Task<int> RunLiveModeAsync(
        string sessionId, SessionSummary session, AgentHubApiClient apiClient,
        SseStreamReader sseReader, ApprovalPromptHandler? approvalHandler, CancellationToken ct)
    {
        var lines = new List<string>(MaxBufferLines);

        while (!ct.IsCancellationRequested)
        {
            SessionEvent? pendingApproval = null;
            bool inputRequested = false;

            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("Event");
            table.Title = new TableTitle(
                $"Session [bold]{Truncate(sessionId, 8)}[/] | {FormatState(session.State)} | Host: {session.Node ?? "auto"} | Agent: {session.Backend}");

            // Pre-populate table with buffered lines from previous iteration
            RebuildTable(table, lines);

            try
            {
                await AnsiConsole.Live(table).StartAsync(async ctx =>
                {
                    ctx.Refresh();

                    await foreach (var (_, evt) in sseReader.StreamSessionEventsAsync(sessionId, ct: ct))
                    {
                        // Check for 'i' hotkey to enter input mode
                        if (Console.KeyAvailable && Console.ReadKey(true).KeyChar == 'i')
                        {
                            inputRequested = true;
                            return; // Exit Live context, same as approval pattern
                        }

                        if (evt.Kind == SessionEventKind.Heartbeat)
                            continue;

                        // If this is an approval request and we have a handler, exit Live to prompt
                        if (evt.Kind == SessionEventKind.ApprovalRequest && approvalHandler is not null)
                        {
                            pendingApproval = evt;
                            return; // Exit Live context so we can show interactive prompt
                        }

                        var formatted = FormatEvent(evt);
                        lines.Add(formatted);

                        // Trim buffer
                        while (lines.Count > MaxBufferLines)
                            lines.RemoveAt(0);

                        // Rebuild table with latest lines (show last 30 for display)
                        RebuildTable(table, lines);

                        // Update title with duration
                        var duration = DateTimeOffset.UtcNow - session.CreatedUtc;
                        table.Title = new TableTitle(
                            $"Session [bold]{Truncate(sessionId, 8)}[/] | {FormatState(session.State)} | Duration: {duration:hh\\:mm\\:ss}");

                        ctx.Refresh();
                    }
                });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }

            if (pendingApproval is not null)
            {
                // Handle approval outside of Live context (Spectre.Console Pitfall 6)
                await approvalHandler!.HandleApprovalEventAsync(pendingApproval, ct);
                AnsiConsole.MarkupLine("[dim]Resuming watch...[/]");
                continue; // Restart Live display
            }

            if (inputRequested)
            {
                AnsiConsole.Markup("[yellow]Input> [/]");
                var text = Console.ReadLine();
                if (!string.IsNullOrEmpty(text))
                {
                    var delivered = await apiClient.SendInputAsync(sessionId, text, ct, isFollowUp: true);
                    if (delivered)
                        AnsiConsole.MarkupLine("[green]Steering delivered.[/]");
                    else
                        AnsiConsole.MarkupLine("[yellow]Warning: Delivery unconfirmed.[/]");

                    if (CheckRapidFire())
                        AnsiConsole.MarkupLine("[yellow]Warning: Sending multiple commands rapidly -- agent may not process them in order[/]");
                }
                inputRequested = false;
                continue; // Restart Live display
            }

            break; // Stream ended naturally
        }

        AnsiConsole.MarkupLine($"\nDetached from session [bold]{Truncate(sessionId, 8)}[/]. Session continues running.");
        return 0;
    }

    private static void RebuildTable(Table table, List<string> lines)
    {
        table.Rows.Clear();
        var displayLines = lines.Count > 30 ? lines.Skip(lines.Count - 30) : lines;
        foreach (var line in displayLines)
            table.AddRow(new Markup(Markup.Escape(line).Replace(
                "[red]", "").Replace("[/]", ""))); // safe fallback
    }

    internal static string FormatEvent(SessionEvent evt)
    {
        var ts = evt.TsUtc.ToLocalTime().ToString("HH:mm:ss");
        var prefix = evt.Kind switch
        {
            SessionEventKind.StdErr => $"[red]{ts} ERR[/] ",
            SessionEventKind.StateChanged => $"[yellow]{ts} STATE[/] ",
            SessionEventKind.ApprovalRequest => $"[bold red]{ts} APPROVAL[/] ",
            SessionEventKind.Info => $"[blue]{ts} INFO[/] ",
            SessionEventKind.Threat => $"[red]{ts} THREAT[/] ",
            SessionEventKind.SessionCompleted => $"[green]{ts} DONE[/] ",
            SessionEventKind.SteeringInput => $"[cyan]{ts} STEER>[/] ",
            SessionEventKind.SteeringDelivered => $"[green]{ts} DELIVERED[/] ",
            _ => $"{ts} ",
        };
        return $"{prefix}{evt.Data}";
    }

    private static bool CheckRapidFire()
    {
        var now = DateTimeOffset.UtcNow;
        s_recentInputs.Enqueue(now);

        while (s_recentInputs.Count > 0 && now - s_recentInputs.Peek() > RapidFireWindow)
            s_recentInputs.Dequeue();

        return s_recentInputs.Count >= RapidFireThreshold;
    }

    private static string FormatState(SessionState state) => state switch
    {
        SessionState.Running => "[green]Running[/]",
        SessionState.Failed => "[red]Failed[/]",
        SessionState.Stopped => "[grey]Stopped[/]",
        SessionState.Pending => "[yellow]Pending[/]",
        _ => state.ToString()
    };

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
