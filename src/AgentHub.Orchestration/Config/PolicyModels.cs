namespace AgentHub.Orchestration.Config;

/// <summary>
/// Legacy skill policy document. Kept for backward compatibility with JSON-only configs.
/// </summary>
public sealed record SkillPolicyDocument(
    string Name,
    string[] EnabledSkills,
    string[] DisabledSkills,
    string[]? ElevatedSkills = null,
    string[]? Notes = null);

/// <summary>
/// Trust tier for action-level gating.
/// </summary>
public enum TrustTier
{
    /// <summary>Action is always allowed without prompting.</summary>
    AlwaysAllow = 0,
    /// <summary>Action requires human approval before proceeding.</summary>
    Prompt = 1,
    /// <summary>Action is always denied.</summary>
    AlwaysDeny = 2
}

/// <summary>
/// Scoped policy configuration supporting the 4-level hierarchy:
/// default -> host -> project -> task (last wins for scalars).
/// </summary>
public sealed class ScopedPolicyConfig
{
    public string Name { get; set; } = string.Empty;
    public string Scope { get; set; } = "default";
    public string? ScopeId { get; set; }
    public Dictionary<string, TrustTier> TrustTiers { get; set; } = new();
    public string[] EnabledSkills { get; set; } = [];
    public string[] DisabledSkills { get; set; } = [];
    public string[] ElevatedSkills { get; set; } = [];
    public int? DefaultTimeoutSeconds { get; set; }
    public string? DefaultTimeoutAction { get; set; }
    public bool SkipPermissionPrompts { get; set; }
    public string[]? DisallowedTools { get; set; }

    /// <summary>
    /// Markdown body content preserved from .md config files.
    /// Passed to agent as additional context/instructions.
    /// </summary>
    public string? Instructions { get; set; }
}

/// <summary>
/// Default trust tier definitions per research recommendations.
/// </summary>
public static class DefaultTrustTiers
{
    public static readonly Dictionary<string, TrustTier> Defaults = new()
    {
        // Always allow: read-only operations
        ["Read"] = TrustTier.AlwaysAllow,
        ["Glob"] = TrustTier.AlwaysAllow,
        ["Grep"] = TrustTier.AlwaysAllow,
        ["LS"] = TrustTier.AlwaysAllow,
        ["WebSearch"] = TrustTier.AlwaysAllow,

        // Prompt: write operations
        ["Write"] = TrustTier.Prompt,
        ["Edit"] = TrustTier.Prompt,
        ["Bash"] = TrustTier.Prompt,
        ["WebFetch"] = TrustTier.Prompt,

        // Always deny: destructive operations
        ["rm -rf /"] = TrustTier.AlwaysDeny,
        ["format"] = TrustTier.AlwaysDeny,
        ["shutdown"] = TrustTier.AlwaysDeny
    };
}
