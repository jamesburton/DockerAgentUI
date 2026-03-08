using System.Text.Json;
using AgentHub.Cli.Api;
using AgentHub.Cli.Config;
using AgentHub.Cli.Output;
using AgentHub.Contracts;
using Spectre.Console;

namespace AgentHub.Cli.Commands;

/// <summary>
/// Implements `ah watch` - fleet-wide live overview of all running sessions.
/// </summary>
public static class WatchCommand
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    public static async Task<int> ExecuteAsync(
        bool jsonMode,
        AgentHubApiClient apiClient,
        SseStreamReader sseReader,
        CliConfig config,
        IOutputFormatter formatter,
        CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        if (jsonMode)
        {
            return await RunJsonModeAsync(sseReader, cts.Token);
        }

        return await RunLiveModeAsync(apiClient, sseReader, config, cts.Token);
    }

    private static async Task<int> RunJsonModeAsync(SseStreamReader sseReader, CancellationToken ct)
    {
        try
        {
            await foreach (var (_, evt) in sseReader.StreamFleetEventsAsync(ct: ct))
            {
                Console.WriteLine(JsonSerializer.Serialize(evt, s_json));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        return 0;
    }

    private static async Task<int> RunLiveModeAsync(
        AgentHubApiClient apiClient, SseStreamReader sseReader, CliConfig config, CancellationToken ct)
    {
        // Track sessions by ID
        var sessions = new Dictionary<string, SessionRow>();

        // Load initial session list
        var (items, _) = await apiClient.GetSessionsAsync(state: "Running", ct: ct);
        foreach (var s in items)
        {
            sessions[s.SessionId] = new SessionRow(
                s.SessionId, s.State.ToString(), s.Backend, s.Node ?? "auto",
                s.CreatedUtc, "");
        }

        var table = BuildTable(sessions);

        try
        {
            await AnsiConsole.Live(table).StartAsync(async ctx =>
            {
                ctx.Refresh();

                // Start periodic refresh in background
                var refreshTask = PeriodicRefreshAsync(apiClient, sessions, table, ctx, config.WatchRefreshMs, ct);

                // SSE event stream
                await foreach (var (_, evt) in sseReader.StreamFleetEventsAsync(ct: ct))
                {
                    UpdateFromEvent(sessions, evt);
                    RebuildTable(table, sessions);
                    ctx.Refresh();
                }

                await refreshTask;
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }

        AnsiConsole.MarkupLine("\nFleet watch stopped.");
        return 0;
    }

    private static async Task PeriodicRefreshAsync(
        AgentHubApiClient apiClient, Dictionary<string, SessionRow> sessions,
        Table table, LiveDisplayContext ctx, int refreshMs, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(refreshMs, ct);
                var (items, _) = await apiClient.GetSessionsAsync(ct: ct);
                foreach (var s in items)
                {
                    sessions[s.SessionId] = sessions.GetValueOrDefault(s.SessionId) is { } existing
                        ? existing with { State = s.State.ToString() }
                        : new SessionRow(s.SessionId, s.State.ToString(), s.Backend, s.Node ?? "auto", s.CreatedUtc, "");
                }
                RebuildTable(table, sessions);
                ctx.Refresh();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private static void UpdateFromEvent(Dictionary<string, SessionRow> sessions, SessionEvent evt)
    {
        var id = evt.SessionId;
        if (sessions.TryGetValue(id, out var row))
        {
            sessions[id] = row with
            {
                State = evt.Kind == SessionEventKind.StateChanged ? evt.Data : row.State,
                LastEvent = $"{evt.Kind}: {Truncate(evt.Data, 30)}"
            };
        }
        else
        {
            sessions[id] = new SessionRow(id, "Running", "", "", DateTimeOffset.UtcNow,
                $"{evt.Kind}: {Truncate(evt.Data, 30)}");
        }
    }

    private static Table BuildTable(Dictionary<string, SessionRow> sessions)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.Title = new TableTitle("[bold]Fleet Overview[/] (Ctrl+C to exit)");
        table.AddColumn("ID");
        table.AddColumn("State");
        table.AddColumn("Agent");
        table.AddColumn("Host");
        table.AddColumn("Duration");
        table.AddColumn("Last Event");

        foreach (var (_, row) in sessions)
            AddRow(table, row);

        return table;
    }

    private static void RebuildTable(Table table, Dictionary<string, SessionRow> sessions)
    {
        table.Rows.Clear();
        foreach (var (_, row) in sessions)
            AddRow(table, row);
    }

    private static void AddRow(Table table, SessionRow row)
    {
        var duration = (DateTimeOffset.UtcNow - row.CreatedUtc).ToString(@"hh\:mm\:ss");
        table.AddRow(
            Markup.Escape(Truncate(row.Id, 8)),
            FormatState(row.State),
            Markup.Escape(row.Agent),
            Markup.Escape(row.Host),
            duration,
            Markup.Escape(row.LastEvent));
    }

    private static string FormatState(string state) => state.ToLowerInvariant() switch
    {
        "running" => "[green]Running[/]",
        "failed" => "[red]Failed[/]",
        "stopped" => "[grey]Stopped[/]",
        "pending" => "[yellow]Pending[/]",
        _ => state
    };

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private sealed record SessionRow(
        string Id, string State, string Agent, string Host,
        DateTimeOffset CreatedUtc, string LastEvent);
}
