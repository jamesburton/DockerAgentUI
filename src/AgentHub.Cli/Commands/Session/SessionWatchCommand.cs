using System.Text.Json;
using AgentHub.Cli.Api;
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

    public static async Task<int> ExecuteAsync(
        string sessionId,
        bool jsonMode,
        AgentHubApiClient apiClient,
        SseStreamReader sseReader,
        IOutputFormatter formatter,
        CancellationToken ct)
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

        return await RunLiveModeAsync(sessionId, session, sseReader, cts.Token);
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
        string sessionId, SessionSummary session, SseStreamReader sseReader, CancellationToken ct)
    {
        var lines = new List<string>(MaxBufferLines);

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Event");
        table.Title = new TableTitle(
            $"Session [bold]{Truncate(sessionId, 8)}[/] | {FormatState(session.State)} | Host: {session.Node ?? "auto"} | Agent: {session.Backend}");

        try
        {
            await AnsiConsole.Live(table).StartAsync(async ctx =>
            {
                ctx.Refresh();

                await foreach (var (_, evt) in sseReader.StreamSessionEventsAsync(sessionId, ct: ct))
                {
                    if (evt.Kind == SessionEventKind.Heartbeat)
                        continue;

                    var formatted = FormatEvent(evt);
                    lines.Add(formatted);

                    // Trim buffer
                    while (lines.Count > MaxBufferLines)
                        lines.RemoveAt(0);

                    // Rebuild table with latest lines (show last 30 for display)
                    table.Rows.Clear();
                    var displayLines = lines.Count > 30 ? lines.Skip(lines.Count - 30) : lines;
                    foreach (var line in displayLines)
                        table.AddRow(new Markup(Markup.Escape(line).Replace(
                            "[red]", "").Replace("[/]", ""))); // safe fallback

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
            // Clean detach
        }

        AnsiConsole.MarkupLine($"\nDetached from session [bold]{Truncate(sessionId, 8)}[/]. Session continues running.");
        return 0;
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
            _ => $"{ts} ",
        };
        return $"{prefix}{evt.Data}";
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
