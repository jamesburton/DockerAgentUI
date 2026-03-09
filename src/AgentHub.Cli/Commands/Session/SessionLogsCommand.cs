using System.Text.Json;
using AgentHub.Cli.Api;
using AgentHub.Cli.Output;
using AgentHub.Contracts;
using Spectre.Console;

namespace AgentHub.Cli.Commands.Session;

/// <summary>
/// Implements `ah session logs &lt;id&gt;` - view session output history with optional follow mode.
/// </summary>
public static class SessionLogsCommand
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    public static async Task<int> ExecuteAsync(
        string sessionId,
        bool all,
        int tail,
        bool follow,
        string? kind,
        bool jsonMode,
        AgentHubApiClient apiClient,
        SseStreamReader sseReader,
        IOutputFormatter formatter,
        CancellationToken ct)
    {
        // Verify session exists
        var session = await apiClient.GetSessionAsync(sessionId, ct);
        if (session is null)
        {
            formatter.WriteError($"Session '{sessionId}' not found.");
            return 1;
        }

        // Load history
        var events = await LoadHistoryAsync(sessionId, all, tail, kind, apiClient, ct);

        if (jsonMode && !follow)
        {
            Console.WriteLine(JsonSerializer.Serialize(events, s_json));
            return 0;
        }

        // Render history
        foreach (var evt in events)
        {
            if (jsonMode)
            {
                Console.WriteLine(JsonSerializer.Serialize(evt, s_json));
            }
            else
            {
                RenderEvent(evt);
            }
        }

        if (!follow)
            return 0;

        // Follow mode: switch to SSE streaming
        string? lastEventId = null;
        if (events.Count > 0 && events[^1].Meta?.TryGetValue("eventId", out var eid) == true)
            lastEventId = eid;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            await foreach (var (eventId, evt) in sseReader.StreamSessionEventsAsync(sessionId, lastEventId, cts.Token))
            {
                if (kind is not null && !evt.Kind.ToString().Equals(kind, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (jsonMode)
                    Console.WriteLine(JsonSerializer.Serialize(evt, s_json));
                else
                    RenderEvent(evt);
            }
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
        {
            // Clean detach
        }

        AnsiConsole.MarkupLine($"\nDetached from session logs. Session continues running.");
        return 0;
    }

    private static async Task<List<SessionEvent>> LoadHistoryAsync(
        string sessionId, bool all, int tail, string? kind,
        AgentHubApiClient apiClient, CancellationToken ct)
    {
        if (all)
        {
            // Paginate through all pages
            var allEvents = new List<SessionEvent>();
            int page = 1;
            const int pageSize = 100;

            while (true)
            {
                var (batch, _) = await apiClient.GetSessionHistoryAsync(sessionId, page, pageSize, kind, ct);
                allEvents.AddRange(batch);
                if (batch.Count < pageSize)
                    break;
                page++;
            }

            return allEvents;
        }
        else
        {
            // Get tail N events - request a page that covers it
            var (events, _) = await apiClient.GetSessionHistoryAsync(sessionId, 1, tail, kind, ct);
            return events;
        }
    }

    private static void RenderEvent(SessionEvent evt)
    {
        var ts = evt.TsUtc.ToLocalTime().ToString("HH:mm:ss");
        var (color, label) = evt.Kind switch
        {
            SessionEventKind.StdErr => ("red", "ERR"),
            SessionEventKind.StateChanged => ("yellow", "STATE"),
            SessionEventKind.ApprovalRequest => ("red", "APPROVAL"),
            SessionEventKind.Info => ("blue", "INFO"),
            SessionEventKind.Threat => ("red", "THREAT"),
            SessionEventKind.SessionCompleted => ("green", "DONE"),
            SessionEventKind.Heartbeat => ("grey", "BEAT"),
            _ => ("default", evt.Kind.ToString().ToUpperInvariant()),
        };

        if (color == "default")
            AnsiConsole.MarkupLine($"{Markup.Escape(ts)} {Markup.Escape(evt.Data)}");
        else
            AnsiConsole.MarkupLine($"[{color}]{Markup.Escape(ts)} {Markup.Escape(label)}[/] {Markup.Escape(evt.Data)}");

        if (evt.Meta is { Count: > 0 })
        {
            var metaPairs = string.Join(" ", evt.Meta.Select(kv => $"{kv.Key}={kv.Value}"));
            AnsiConsole.MarkupLine($"[grey]         meta: {Markup.Escape(metaPairs)}[/]");
        }
    }
}
