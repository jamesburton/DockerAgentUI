using Spectre.Console;

namespace AgentHub.Cli.Output;

public interface IOutputFormatter
{
    void WriteTable<T>(IEnumerable<T> items, Action<Table, T> addRow, params string[] columns);
    void WriteObject<T>(T item);
    void WriteError(string message);
    void WriteSuccess(string message);
}
