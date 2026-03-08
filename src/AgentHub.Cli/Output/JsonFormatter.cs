using System.Text.Json;
using Spectre.Console;

namespace AgentHub.Cli.Output;

public sealed class JsonFormatter : IOutputFormatter
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public void WriteTable<T>(IEnumerable<T> items, Action<Table, T> addRow, params string[] columns)
    {
        // In JSON mode, ignore the table builder and serialize items directly
        var json = JsonSerializer.Serialize(items, s_json);
        Console.WriteLine(json);
    }

    public void WriteObject<T>(T item)
    {
        var json = JsonSerializer.Serialize(item, s_json);
        Console.WriteLine(json);
    }

    public void WriteError(string message)
    {
        var json = JsonSerializer.Serialize(new { error = message }, s_json);
        Console.Error.WriteLine(json);
    }

    public void WriteSuccess(string message)
    {
        var json = JsonSerializer.Serialize(new { success = message }, s_json);
        Console.WriteLine(json);
    }
}
