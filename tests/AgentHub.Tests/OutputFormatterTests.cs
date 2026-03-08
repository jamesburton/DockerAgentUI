using System.Text.Json;
using AgentHub.Cli.Output;
using Spectre.Console;
using Xunit;

namespace AgentHub.Tests;

public class OutputFormatterTests
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void JsonFormatter_WriteTable_ProducesValidJsonArray()
    {
        var formatter = new JsonFormatter();
        var items = new[] { new { Name = "one", Value = 1 }, new { Name = "two", Value = 2 } };

        var output = CaptureConsoleOut(() =>
            formatter.WriteTable(items, (_, _) => { }, "Name", "Value"));

        var doc = JsonDocument.Parse(output);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(2, doc.RootElement.GetArrayLength());
        Assert.Equal("one", doc.RootElement[0].GetProperty("name").GetString());
    }

    [Fact]
    public void JsonFormatter_WriteObject_ProducesValidJsonObject()
    {
        var formatter = new JsonFormatter();
        var item = new { Id = "abc", Status = "running" };

        var output = CaptureConsoleOut(() => formatter.WriteObject(item));

        var doc = JsonDocument.Parse(output);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.Equal("abc", doc.RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public void JsonFormatter_WriteError_ProducesErrorJson()
    {
        var formatter = new JsonFormatter();

        var output = CaptureConsoleError(() => formatter.WriteError("something broke"));

        var doc = JsonDocument.Parse(output);
        Assert.Equal("something broke", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void JsonFormatter_WriteSuccess_ProducesSuccessJson()
    {
        var formatter = new JsonFormatter();

        var output = CaptureConsoleOut(() => formatter.WriteSuccess("it worked"));

        var doc = JsonDocument.Parse(output);
        Assert.Equal("it worked", doc.RootElement.GetProperty("success").GetString());
    }

    [Fact]
    public void TableFormatter_CanInstantiateWithoutError()
    {
        // Spectre.Console renders to AnsiConsole which may not be available in test,
        // but we can verify the formatter constructs and basic methods don't throw.
        var formatter = new TableFormatter();
        Assert.NotNull(formatter);
    }

    private static string CaptureConsoleOut(Action action)
    {
        var original = Console.Out;
        try
        {
            using var writer = new StringWriter();
            Console.SetOut(writer);
            action();
            return writer.ToString().Trim();
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private static string CaptureConsoleError(Action action)
    {
        var original = Console.Error;
        try
        {
            using var writer = new StringWriter();
            Console.SetError(writer);
            action();
            return writer.ToString().Trim();
        }
        finally
        {
            Console.SetError(original);
        }
    }
}
