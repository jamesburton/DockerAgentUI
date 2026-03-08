using System.Text.Json;
using AgentHub.Cli.Api;
using AgentHub.Contracts;
using Spectre.Console;

namespace AgentHub.Cli.Notifications;

/// <summary>
/// Handles inline approval prompts during session watch.
/// Pauses the Live display, shows approval details, and resumes after resolution.
/// </summary>
public sealed class ApprovalPromptHandler
{
    private readonly AgentHubApiClient _apiClient;
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    public ApprovalPromptHandler(AgentHubApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    /// <summary>
    /// Handle an approval request event. This method should be called when the Live display
    /// is NOT active (caller must stop Live first per Spectre.Console Pitfall 6).
    /// </summary>
    public async Task<ApprovalResult> HandleApprovalEventAsync(SessionEvent evt, CancellationToken ct)
    {
        // Ring terminal bell
        Console.Write('\a');

        // Extract approval details
        var approvalId = evt.Meta?.GetValueOrDefault("approvalId") ?? "unknown";
        var action = evt.Data;
        var riskLevel = evt.Meta?.GetValueOrDefault("riskLevel") ?? "unknown";

        // Render approval panel
        var panel = new Panel(new Markup(
            $"[bold]Action:[/] {Markup.Escape(action)}\n" +
            $"[bold]Risk Level:[/] {FormatRisk(riskLevel)}\n" +
            $"[bold]Session:[/] {Markup.Escape(evt.SessionId)}\n" +
            $"[bold]Approval ID:[/] {Markup.Escape(approvalId)}"))
        {
            Header = new PanelHeader("[bold red]Approval Required[/]"),
            Border = BoxBorder.Double
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();

        // Prompt for action
        AnsiConsole.Markup("[bold][A][/]pprove / [bold][R][/]eject / [bold][S][/]kip: ");
        var key = Console.ReadKey(intercept: true);
        AnsiConsole.WriteLine();

        var result = key.Key switch
        {
            ConsoleKey.A => ApprovalResult.Approved,
            ConsoleKey.R => ApprovalResult.Rejected,
            _ => ApprovalResult.Skipped
        };

        if (result != ApprovalResult.Skipped && approvalId != "unknown")
        {
            try
            {
                await _apiClient.ResolveApprovalAsync(approvalId, result == ApprovalResult.Approved, ct);
                AnsiConsole.MarkupLine(result == ApprovalResult.Approved
                    ? "[green]Approved.[/]"
                    : "[red]Rejected.[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Failed to resolve approval: {Markup.Escape(ex.Message)}[/]");
            }
        }
        else if (result == ApprovalResult.Skipped)
        {
            AnsiConsole.MarkupLine("[dim]Skipped.[/]");
        }

        return result;
    }

    private static string FormatRisk(string riskLevel) => riskLevel.ToLowerInvariant() switch
    {
        "high" => "[bold red]HIGH[/]",
        "medium" => "[yellow]Medium[/]",
        "low" => "[green]Low[/]",
        _ => riskLevel
    };
}

public enum ApprovalResult
{
    Approved,
    Rejected,
    Skipped
}
