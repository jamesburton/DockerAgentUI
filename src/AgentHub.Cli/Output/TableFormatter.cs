using System.Text.Json;
using Spectre.Console;

namespace AgentHub.Cli.Output;

public sealed class TableFormatter : IOutputFormatter
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public void WriteTable<T>(IEnumerable<T> items, Action<Table, T> addRow, params string[] columns)
    {
        var table = new Table();
        table.Border(TableBorder.Rounded);
        foreach (var col in columns)
            table.AddColumn(col);

        foreach (var item in items)
            addRow(table, item);

        AnsiConsole.Write(table);
    }

    public void WriteObject<T>(T item)
    {
        var json = JsonSerializer.Serialize(item, s_json);
        var panel = new Panel(json)
        {
            Border = BoxBorder.Rounded
        };
        AnsiConsole.Write(panel);
    }

    public void WriteError(string message)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(message)}");
    }

    public void WriteSuccess(string message)
    {
        AnsiConsole.MarkupLine($"[green]OK:[/] {Markup.Escape(message)}");
    }
}
