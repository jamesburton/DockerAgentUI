using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentHub.Cli.Api;
using AgentHub.Cli.Output;
using AgentHub.Contracts;
using Spectre.Console;

namespace AgentHub.Cli.Commands.Session;

/// <summary>
/// Implements `ah session diff &lt;id&gt;` - display diff stats for a completed worktree session.
/// </summary>
public static class SessionDiffCommand
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    public static async Task<int> ExecuteAsync(
        string sessionId,
        bool detailed,
        AgentHubApiClient apiClient,
        IOutputFormatter formatter,
        CancellationToken ct)
    {
        try
        {
            var response = await apiClient.GetSessionDiffRawAsync(sessionId, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                formatter.WriteError($"Session '{sessionId}' not found.");
                return 1;
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(s_json, ct);
                formatter.WriteError(error?.Error ?? "Bad request");
                return 1;
            }

            response.EnsureSuccessStatusCode();

            var diff = await response.Content.ReadFromJsonAsync<DiffStats>(s_json, ct);
            if (diff is null)
            {
                formatter.WriteError("Failed to parse diff stats response.");
                return 1;
            }

            if (detailed)
            {
                var table = new Table().Border(TableBorder.Rounded);
                table.AddColumn("File");
                table.AddColumn("Status");
                table.AddColumn(new TableColumn("[green]+[/]").RightAligned());
                table.AddColumn(new TableColumn("[red]-[/]").RightAligned());

                foreach (var file in diff.Files)
                {
                    table.AddRow(
                        file.Path,
                        file.Status,
                        $"[green]+{file.Insertions}[/]",
                        $"[red]-{file.Deletions}[/]");
                }

                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine($"\n[bold]{diff.Summary}[/]");
            }
            else
            {
                AnsiConsole.WriteLine(diff.Summary);
            }

            return 0;
        }
        catch (HttpRequestException ex)
        {
            formatter.WriteError($"API error: {ex.Message}");
            return 1;
        }
    }

    internal sealed record ErrorResponse(string? Error);
}
