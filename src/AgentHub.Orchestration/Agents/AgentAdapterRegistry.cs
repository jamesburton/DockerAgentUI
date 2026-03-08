namespace AgentHub.Orchestration.Agents;

/// <summary>
/// Registry for resolving agent adapters by type string.
/// Uses DI-injected collection of all IAgentAdapter implementations.
/// </summary>
public sealed class AgentAdapterRegistry
{
    private readonly Dictionary<string, IAgentAdapter> _adapters;

    public AgentAdapterRegistry(IEnumerable<IAgentAdapter> adapters)
    {
        _adapters = adapters.ToDictionary(
            a => a.AgentType,
            a => a,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolve an adapter by agent type string (case-insensitive).
    /// Returns null if no adapter is registered for the given type.
    /// </summary>
    public IAgentAdapter? GetAdapter(string agentType)
    {
        return _adapters.TryGetValue(agentType, out var adapter) ? adapter : null;
    }

    /// <summary>
    /// Returns all registered agent type strings.
    /// </summary>
    public IReadOnlyList<string> GetSupportedTypes()
    {
        return _adapters.Keys.ToList();
    }
}
