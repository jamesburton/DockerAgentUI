using System.Text.Json.Serialization;
using AgentHub.Contracts;

namespace AgentHub.Orchestration.Agents;

/// <summary>
/// Request to start an agent process.
/// </summary>
public sealed record AgentStartRequest(
    string SessionId,
    string WorkingDirectory,
    string Prompt,
    Dictionary<string, string>? Environment = null,
    AgentPermissions? Permissions = null,
    AgentAdapterConfig? AgentConfig = null);

/// <summary>
/// Configuration for an agent adapter, loaded from config/agents/*.json.
/// Uses string agent type for flexible matching (e.g., "claude-code").
/// </summary>
public sealed record AgentAdapterConfig
{
    [JsonPropertyName("agentType")]
    public string AgentType { get; init; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = "";

    [JsonPropertyName("cliCommand")]
    public string CliCommand { get; init; } = "";

    [JsonPropertyName("outputFormat")]
    public string OutputFormat { get; init; } = "";

    [JsonPropertyName("defaultPermissions")]
    public AgentPermissionConfig? DefaultPermissions { get; init; }

    [JsonPropertyName("cliVersionPattern")]
    public string? CliVersionPattern { get; init; }
}

/// <summary>
/// JSON-serializable permission configuration for agent config files.
/// </summary>
public sealed record AgentPermissionConfig
{
    [JsonPropertyName("skipPermissionPrompts")]
    public bool SkipPermissionPrompts { get; init; }

    [JsonPropertyName("allowedTools")]
    public string[]? AllowedTools { get; init; }

    [JsonPropertyName("disallowedTools")]
    public string[]? DisallowedTools { get; init; }

    /// <summary>
    /// Convert to the Contracts AgentPermissions record.
    /// </summary>
    public AgentPermissions ToAgentPermissions() =>
        new(SkipPermissionPrompts, AllowedTools, DisallowedTools);
}

/// <summary>
/// Merges per-session permission overrides with agent-type defaults.
/// Session values win when specified.
/// </summary>
public static class PermissionMerger
{
    public static AgentPermissions Merge(AgentPermissions? sessionOverrides, AgentPermissionConfig? agentDefaults)
    {
        if (sessionOverrides is null && agentDefaults is null)
            return new AgentPermissions();

        if (sessionOverrides is null)
            return agentDefaults!.ToAgentPermissions();

        if (agentDefaults is null)
            return sessionOverrides;

        // Session overrides win for SkipPermissionPrompts (any explicit true wins)
        var skip = sessionOverrides.SkipPermissionPrompts || agentDefaults.SkipPermissionPrompts;

        // Session overrides win for tool lists when provided; fall back to defaults
        var allowed = sessionOverrides.AllowedTools is { Length: > 0 }
            ? sessionOverrides.AllowedTools
            : agentDefaults.AllowedTools;

        var disallowed = sessionOverrides.DisallowedTools is { Length: > 0 }
            ? sessionOverrides.DisallowedTools
            : agentDefaults.DisallowedTools;

        return new AgentPermissions(skip, allowed, disallowed);
    }
}
