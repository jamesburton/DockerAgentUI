using AgentHub.Cli.Api;
using AgentHub.Cli.Output;
using AgentHub.Contracts;
using Spectre.Console;

namespace AgentHub.Cli.Commands.Session;

public static class SessionListCommand
{
    public static async Task<int> ExecuteAsync(
        AgentHubApiClient client,
        IOutputFormatter formatter,
        bool verbose,
        bool isJson,
        int take,
        string? stateFilter,
        CancellationToken ct)
    {
        var (items, _) = await client.GetSessionsAsync(take: take, state: stateFilter, ct: ct);

        // JSON mode: always flat output
        if (isJson)
        {
            formatter.WriteTable(items, (table, s) =>
            {
                var id = verbose ? s.SessionId : Truncate(s.SessionId, 8);
                var host = s.Node ?? "(auto)";
                var created = s.CreatedUtc.ToLocalTime().ToString("HH:mm:ss");
                var dur = FormatDuration(s);
                table.AddRow(id, s.State.ToString(), s.Backend, host, created, dur);
            }, "ID", "State", "Agent", "Host", "Created", "Duration");
            return 0;
        }

        // Check for parent-child relationships
        var hasHierarchy = items.Any(s => s.ParentSessionId is not null);

        if (hasHierarchy)
        {
            RenderTree(items, verbose);
        }
        else
        {
            // Flat table fallback (no parent-child relationships)
            RenderFlatTable(items, verbose, formatter);
        }

        return 0;
    }

    private static void RenderTree(List<SessionSummary> items, bool verbose)
    {
        // Build lookup: parentId -> children
        var childrenByParent = items
            .Where(s => s.ParentSessionId is not null)
            .GroupBy(s => s.ParentSessionId!)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Root sessions: no parent
        var roots = items.Where(s => s.ParentSessionId is null).ToList();

        // Also include orphaned children whose parent isn't in the list
        var knownIds = new HashSet<string>(items.Select(s => s.SessionId));
        var orphans = items
            .Where(s => s.ParentSessionId is not null && !knownIds.Contains(s.ParentSessionId))
            .ToList();

        var tree = new Tree("[bold]Sessions[/]");

        foreach (var root in roots)
        {
            var node = tree.AddNode(FormatSessionNode(root, verbose, isChild: false));
            AddChildren(node, root.SessionId, childrenByParent, verbose, depth: 1, maxDepth: 3);
        }

        // Show orphans at root level with a note
        foreach (var orphan in orphans)
        {
            tree.AddNode(FormatSessionNode(orphan, verbose, isChild: true) + " [dim](orphan)[/]");
        }

        AnsiConsole.Write(tree);
    }

    private static void AddChildren(
        TreeNode parentNode,
        string parentId,
        Dictionary<string, List<SessionSummary>> childrenByParent,
        bool verbose,
        int depth,
        int maxDepth)
    {
        if (depth > maxDepth) return;
        if (!childrenByParent.TryGetValue(parentId, out var children)) return;

        foreach (var child in children)
        {
            var childNode = parentNode.AddNode(FormatSessionNode(child, verbose, isChild: true));
            AddChildren(childNode, child.SessionId, childrenByParent, verbose, depth + 1, maxDepth);
        }
    }

    private static string FormatSessionNode(SessionSummary session, bool verbose, bool isChild)
    {
        var id = verbose ? session.SessionId : Truncate(session.SessionId, 8);
        var state = FormatState(session.State);
        var host = session.Node ?? "(auto)";
        var dur = FormatDuration(session);
        var style = isChild ? "[dim]" : "";
        var styleEnd = isChild ? "[/]" : "";

        return $"{style}{Markup.Escape(id)} {state} {Markup.Escape(session.Backend)} @ {Markup.Escape(host)} ({dur}){styleEnd}";
    }

    private static void RenderFlatTable(List<SessionSummary> items, bool verbose, IOutputFormatter formatter)
    {
        formatter.WriteTable(items, (table, s) =>
        {
            var id = verbose ? s.SessionId : Truncate(s.SessionId, 8);
            var st = FormatState(s.State);
            var host = s.Node ?? "(auto)";
            var created = s.CreatedUtc.ToLocalTime().ToString("HH:mm:ss");
            var dur = FormatDuration(s);

            if (verbose)
                table.AddRow(id, st, s.Backend, host, s.Backend, created, dur);
            else
                table.AddRow(id, st, s.Backend, host, created, dur);
        },
        verbose
            ? ["ID", "State", "Agent", "Host", "Backend", "Created", "Duration"]
            : ["ID", "State", "Agent", "Host", "Created", "Duration"]);
    }

    private static string FormatState(SessionState state) => state switch
    {
        SessionState.Running => "[green]Running[/]",
        SessionState.Failed => "[red]Failed[/]",
        SessionState.Stopped => "[grey]Stopped[/]",
        SessionState.Pending => "[yellow]Pending[/]",
        _ => state.ToString()
    };

    private static string FormatDuration(SessionSummary session)
    {
        var elapsed = DateTimeOffset.UtcNow - session.CreatedUtc;
        return elapsed.ToString(@"hh\:mm\:ss");
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
