namespace AgentHub.Orchestration.Config;

/// <summary>
/// Merges multiple ScopedPolicyConfig instances using the scoping hierarchy:
/// default -> host -> project -> task (last wins for scalars).
///
/// Merge semantics:
/// - Scalars (Name, Scope, DefaultTimeoutSeconds, DefaultTimeoutAction): last non-null wins
/// - TrustTiers dictionary: merge with last-wins per key
/// - EnabledSkills/DisabledSkills: last non-empty wins (session overrides per Phase 1 decision)
/// - ElevatedSkills: union
/// - SkipPermissionPrompts: OR logic (any true wins, per Phase 1 decision)
/// - DisallowedTools: union (any scope can deny)
/// </summary>
public sealed class ConfigScopeMerger
{
    /// <summary>
    /// Merge configs in order (first = lowest priority, last = highest priority).
    /// </summary>
    public ScopedPolicyConfig Merge(IReadOnlyList<ScopedPolicyConfig> configs)
    {
        if (configs.Count == 0)
            return new ScopedPolicyConfig();

        if (configs.Count == 1)
            return configs[0];

        var result = new ScopedPolicyConfig();

        foreach (var config in configs)
        {
            // Scalars: last non-null/non-default wins
            if (!string.IsNullOrEmpty(config.Name))
                result.Name = config.Name;

            if (!string.IsNullOrEmpty(config.Scope))
                result.Scope = config.Scope;

            if (!string.IsNullOrEmpty(config.ScopeId))
                result.ScopeId = config.ScopeId;

            if (config.DefaultTimeoutSeconds is not null)
                result.DefaultTimeoutSeconds = config.DefaultTimeoutSeconds;

            if (config.DefaultTimeoutAction is not null)
                result.DefaultTimeoutAction = config.DefaultTimeoutAction;

            if (config.Instructions is not null)
                result.Instructions = config.Instructions;

            // EnabledSkills/DisabledSkills: last non-empty wins (session overrides)
            if (config.EnabledSkills.Length > 0)
                result.EnabledSkills = config.EnabledSkills;

            if (config.DisabledSkills.Length > 0)
                result.DisabledSkills = config.DisabledSkills;

            // ElevatedSkills: union
            if (config.ElevatedSkills.Length > 0)
            {
                result.ElevatedSkills = result.ElevatedSkills
                    .Union(config.ElevatedSkills, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            // TrustTiers: merge with last-wins per key
            foreach (var (key, tier) in config.TrustTiers)
            {
                result.TrustTiers[key] = tier;
            }

            // SkipPermissionPrompts: OR logic
            if (config.SkipPermissionPrompts)
                result.SkipPermissionPrompts = true;

            // DisallowedTools: union (any scope can deny)
            if (config.DisallowedTools is { Length: > 0 })
            {
                var existing = result.DisallowedTools ?? [];
                result.DisallowedTools = existing
                    .Union(config.DisallowedTools, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }

        return result;
    }
}

/// <summary>
/// Discovers config files across the scoping hierarchy directories.
/// </summary>
public static class ConfigDiscovery
{
    private static readonly string[] s_extensions = [".md", ".yaml", ".yml", ".json"];

    /// <summary>
    /// Find config files across scope directories with supported extensions.
    /// Returns ordered list: default first, task last.
    /// </summary>
    public static IReadOnlyList<string> FindConfigFiles(
        string configName, string configRoot,
        string? hostId = null, string? projectId = null, string? taskId = null)
    {
        var paths = new List<string>();

        var scopes = new List<string> { Path.Combine(configRoot, "defaults") };
        if (hostId is not null) scopes.Add(Path.Combine(configRoot, "hosts", hostId));
        if (projectId is not null) scopes.Add(Path.Combine(configRoot, "projects", projectId));
        if (taskId is not null) scopes.Add(Path.Combine(configRoot, "tasks", taskId));

        foreach (var scope in scopes)
        {
            if (!Directory.Exists(scope))
                continue;

            foreach (var ext in s_extensions)
            {
                var path = Path.Combine(scope, configName + ext);
                if (File.Exists(path))
                    paths.Add(path);
            }
        }

        return paths;
    }
}
