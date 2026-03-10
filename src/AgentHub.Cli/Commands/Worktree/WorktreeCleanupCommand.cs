using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentHub.Cli.Api;
using AgentHub.Cli.Output;
using Spectre.Console;

namespace AgentHub.Cli.Commands.Worktree;

/// <summary>
/// Implements `ah worktree cleanup --host &lt;id&gt;` - remove orphaned worktrees from a host.
/// </summary>
public static class WorktreeCleanupCommand
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    public static async Task<int> ExecuteAsync(
        string hostId,
        AgentHubApiClient apiClient,
        IOutputFormatter formatter,
        CancellationToken ct)
    {
        try
        {
            var response = await apiClient.PostWorktreeCleanupRawAsync(hostId, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                formatter.WriteError($"Host '{hostId}' not found.");
                return 1;
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(s_json, ct);
                formatter.WriteError(error?.Error ?? "Bad request");
                return 1;
            }

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<CleanupResponse>(s_json, ct);
            if (result is null)
            {
                formatter.WriteError("Failed to parse cleanup response.");
                return 1;
            }

            if (result.Cleaned == 0)
            {
                AnsiConsole.MarkupLine("[green]No orphaned worktrees found.[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[green]Cleaned {result.Cleaned} orphaned worktree(s):[/]");
                if (result.Paths is not null)
                {
                    foreach (var path in result.Paths)
                    {
                        AnsiConsole.MarkupLine($"  [grey]- {Markup.Escape(path)}[/]");
                    }
                }
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
    internal sealed record CleanupResponse(int Cleaned, string? Message, List<string>? Paths);
}
