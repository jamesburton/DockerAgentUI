using AgentHub.Cli.Api;
using AgentHub.Cli.Config;
using AgentHub.Cli.Output;
using AgentHub.Contracts;
using Spectre.Console;

namespace AgentHub.Cli.Commands.Host;

/// <summary>
/// Implements `ah host status --watch` - periodic refresh of host resource metrics.
/// </summary>
public static class HostStatusCommand
{
    public static async Task<int> ExecuteWatchAsync(
        AgentHubApiClient apiClient,
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

        var table = BuildTable(await apiClient.GetHostsAsync(cts.Token));

        try
        {
            await AnsiConsole.Live(table).StartAsync(async ctx =>
            {
                ctx.Refresh();

                while (!cts.Token.IsCancellationRequested)
                {
                    await Task.Delay(config.WatchRefreshMs, cts.Token);

                    var hosts = await apiClient.GetHostsAsync(cts.Token);
                    RebuildTable(table, hosts);
                    ctx.Refresh();
                }
            });
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested) { }

        AnsiConsole.MarkupLine("\n[dim]Host watch stopped.[/]");
        return 0;
    }

    private static Table BuildTable(List<HostRecord> hosts)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.Title = new TableTitle("[bold]Host Status[/] (Ctrl+C to exit)");
        table.AddColumn("Name");
        table.AddColumn("OS");
        table.AddColumn("CPU%");
        table.AddColumn("Memory");
        table.AddColumn("Agents");
        table.AddColumn("Disk");
        table.AddColumn("Sessions");

        foreach (var h in hosts)
            AddRow(table, h);

        return table;
    }

    private static void RebuildTable(Table table, List<HostRecord> hosts)
    {
        table.Rows.Clear();
        foreach (var h in hosts)
            AddRow(table, h);
    }

    private static void AddRow(Table table, HostRecord h)
    {
        var cpu = h.CpuPercent.HasValue ? FormatCpu(h.CpuPercent.Value) : "--";
        var mem = h.MemUsedMb.HasValue && h.MemTotalMb.HasValue
            ? FormatMem(h.MemUsedMb.Value, h.MemTotalMb.Value) : "--";
        var agents = h.Inventory?.Agents is { Count: > 0 }
            ? string.Join(",", h.Inventory.Agents.Select(a => a.Name))
            : "--";
        var disk = h.Inventory?.DiskFreeGb.HasValue == true
            ? $"{h.Inventory.DiskFreeGb:F1} GB"
            : "--";
        table.AddRow(Markup.Escape(h.DisplayName), Markup.Escape(h.Os), cpu, mem,
            Markup.Escape(agents), Markup.Escape(disk), "--");
    }

    private static string FormatCpu(double cpuPercent)
    {
        var v = $"{cpuPercent:F1}%";
        return cpuPercent switch { > 80 => $"[red]{v}[/]", > 60 => $"[yellow]{v}[/]", _ => $"[green]{v}[/]" };
    }

    private static string FormatMem(long usedMb, long totalMb)
    {
        var pct = totalMb > 0 ? (double)usedMb / totalMb * 100 : 0;
        var v = $"{usedMb}/{totalMb} MB";
        return pct switch { > 90 => $"[red]{v}[/]", > 70 => $"[yellow]{v}[/]", _ => $"[green]{v}[/]" };
    }
}
