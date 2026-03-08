namespace AgentHub.Orchestration.Config;

/// <summary>
/// End-to-end config resolution pipeline that composes ConfigDiscovery + ConfigLoader + ConfigScopeMerger.
/// Discovers config files across the scope hierarchy, loads each one, and merges them into a single
/// ScopedPolicyConfig following the default -> host -> project -> task precedence.
/// </summary>
public sealed class ConfigResolutionService
{
    private readonly ConfigLoader _loader;
    private readonly ConfigScopeMerger _merger;

    public ConfigResolutionService(ConfigLoader loader, ConfigScopeMerger merger)
    {
        _loader = loader;
        _merger = merger;
    }

    /// <summary>
    /// Resolves a scoped policy config by discovering, loading, and merging config files
    /// across the scope hierarchy.
    /// </summary>
    /// <param name="configName">Config file base name (e.g., "policy").</param>
    /// <param name="configRoot">Root directory containing scope subdirectories.</param>
    /// <param name="hostId">Optional host scope identifier.</param>
    /// <param name="projectId">Optional project scope identifier.</param>
    /// <param name="taskId">Optional task scope identifier.</param>
    /// <returns>Merged ScopedPolicyConfig, or a default instance if no files found.</returns>
    public ScopedPolicyConfig Resolve(
        string configName,
        string configRoot,
        string? hostId = null,
        string? projectId = null,
        string? taskId = null)
    {
        var filePaths = ConfigDiscovery.FindConfigFiles(configName, configRoot, hostId, projectId, taskId);

        if (filePaths.Count == 0)
            return new ScopedPolicyConfig();

        var configs = new List<ScopedPolicyConfig>(filePaths.Count);
        foreach (var path in filePaths)
        {
            configs.Add(_loader.Load<ScopedPolicyConfig>(path));
        }

        return _merger.Merge(configs);
    }
}
