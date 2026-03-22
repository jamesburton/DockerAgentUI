using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentHub.Orchestration.Coordinator;

/// <summary>
/// Static utility class for parsing SSH stdout spawn markers.
/// Pure logic, no DI -- follows BranchNameGenerator/DiffStatsParser pattern.
/// </summary>
public static partial class SpawnInterceptor
{
    /// <summary>
    /// Compiled regex for spawn marker: ##AGENTHUB_SPAWN:{json}##
    /// </summary>
    [GeneratedRegex(@"##AGENTHUB_SPAWN:(\{[^#]+\})##")]
    private static partial Regex SpawnMarkerRegex();

    /// <summary>
    /// Quick check if a line contains a spawn marker without full parsing.
    /// </summary>
    public static bool IsSpawnMarker(string line)
        => line.Contains("##AGENTHUB_SPAWN:");

    /// <summary>
    /// Attempts to parse a spawn command from a line of SSH stdout.
    /// Returns null if the line is not a spawn marker or JSON is malformed.
    /// </summary>
    public static SpawnCommand? TryParse(string line)
    {
        var match = SpawnMarkerRegex().Match(line);
        if (!match.Success)
            return null;

        try
        {
            var json = match.Groups[1].Value;
            return JsonSerializer.Deserialize<SpawnCommand>(json, JsonOpts);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
}

/// <summary>
/// Parsed spawn command from SSH stdout marker.
/// </summary>
public sealed record SpawnCommand(
    string Agent,
    string Prompt,
    string? TargetHostId = null,
    bool? AcceptRisk = null,
    bool? Worktree = null);
