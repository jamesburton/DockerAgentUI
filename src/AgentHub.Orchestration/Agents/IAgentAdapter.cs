namespace AgentHub.Orchestration.Agents;

/// <summary>
/// Abstraction for agent CLI adapters. Separates "what agent to run" from
/// "where to run it" (ISessionBackend). Each adapter knows how to invoke
/// a specific agent CLI and parse its output.
/// </summary>
public interface IAgentAdapter
{
    /// <summary>
    /// Unique agent type identifier (e.g., "claude-code").
    /// </summary>
    string AgentType { get; }

    /// <summary>
    /// Starts the agent process and returns a handle for streaming output,
    /// sending input, and stopping the process.
    /// </summary>
    Task<AgentProcess> StartAsync(AgentStartRequest request, CancellationToken ct);

    /// <summary>
    /// Builds the CLI argument list for the given request. Public for testability.
    /// </summary>
    IReadOnlyList<string> BuildCommandArgs(AgentStartRequest request);
}
