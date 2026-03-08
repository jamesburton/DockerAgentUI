using System.Text.Json;
using AgentHub.Cli.Api;
using AgentHub.Cli.Notifications;
using AgentHub.Cli.Output;
using AgentHub.Contracts;
using Spectre.Console;

namespace AgentHub.Cli.Commands;

/// <summary>
/// Implements `ah listen` - background notification stream of notable fleet events.
/// </summary>
public static class ListenCommand
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    // Notable event kinds that trigger notifications
    private static readonly HashSet<SessionEventKind> NotableKinds =
    [
        SessionEventKind.StateChanged,
        SessionEventKind.SessionCompleted,
        SessionEventKind.ApprovalRequest,
        SessionEventKind.Threat
    ];

    public static async Task<int> ExecuteAsync(
        bool jsonMode,
        SseStreamReader sseReader,
        NotificationService notificationService,
        CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        AnsiConsole.MarkupLine("[dim]Listening for fleet events... (Ctrl+C to stop)[/]");

        try
        {
            await foreach (var (_, evt) in sseReader.StreamFleetEventsAsync(ct: cts.Token))
            {
                if (!NotableKinds.Contains(evt.Kind))
                    continue;

                // Record notification for persistence
                var summary = FormatSummary(evt);
                notificationService.RecordNotification(evt.SessionId, evt.Kind, summary);

                if (jsonMode)
                {
                    Console.WriteLine(JsonSerializer.Serialize(evt, s_json));
                }
                else
                {
                    RenderEvent(evt, summary);
                }
            }
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested) { }

        AnsiConsole.MarkupLine("\n[dim]Stopped listening.[/]");
        return 0;
    }

    private static string FormatSummary(SessionEvent evt)
    {
        var sessionShort = evt.SessionId.Length > 8 ? evt.SessionId[..8] : evt.SessionId;
        return evt.Kind switch
        {
            SessionEventKind.SessionCompleted => $"Session {sessionShort} completed",
            SessionEventKind.ApprovalRequest => $"Session {sessionShort} requires approval: {Truncate(evt.Data, 50)}",
            SessionEventKind.Threat => $"Session {sessionShort} threat detected: {Truncate(evt.Data, 50)}",
            SessionEventKind.StateChanged => $"Session {sessionShort} state: {Truncate(evt.Data, 30)}",
            _ => $"Session {sessionShort}: {Truncate(evt.Data, 50)}"
        };
    }

    private static void RenderEvent(SessionEvent evt, string summary)
    {
        var ts = evt.TsUtc.ToLocalTime().ToString("HH:mm:ss");
        var (color, label) = evt.Kind switch
        {
            SessionEventKind.SessionCompleted => ("green", "COMPLETED"),
            SessionEventKind.ApprovalRequest => ("yellow", "APPROVAL"),
            SessionEventKind.Threat => ("red", "THREAT"),
            SessionEventKind.StateChanged when evt.Data.Contains("Failed", StringComparison.OrdinalIgnoreCase)
                => ("red", "FAILED"),
            SessionEventKind.StateChanged => ("blue", "STATE"),
            _ => ("default", evt.Kind.ToString().ToUpperInvariant()),
        };

        if (color == "default")
            AnsiConsole.MarkupLine($"[dim]{Markup.Escape(ts)}[/] {Markup.Escape(summary)}");
        else
            AnsiConsole.MarkupLine($"[dim]{Markup.Escape(ts)}[/] [{color}]{Markup.Escape(label)}[/] {Markup.Escape(summary)}");
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
