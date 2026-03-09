namespace AgentHub.Orchestration.Agents;

/// <summary>
/// Agent adapter for Claude Code CLI.
/// Translates AgentStartRequest into claude CLI argument lists.
/// Execution is handled by SshBackend via HostCommandProtocol.
/// </summary>
public sealed class ClaudeCodeAdapter : IAgentAdapter
{
    public string AgentType => "claude-code";

    public IReadOnlyList<string> BuildCommandArgs(AgentStartRequest request)
    {
        var args = new List<string>();

        // Prompt
        args.Add("-p");
        args.Add(request.Prompt);

        // Output format
        args.Add("--output-format");
        args.Add("stream-json");

        // No session persistence (stateless invocations)
        args.Add("--no-session-persistence");

        // Merge permissions: session overrides win over agent-type defaults
        var permissions = PermissionMerger.Merge(
            request.Permissions,
            request.AgentConfig?.DefaultPermissions);

        // Permission flags
        if (permissions.SkipPermissionPrompts)
        {
            args.Add("--dangerously-skip-permissions");
        }

        if (permissions.AllowedTools is { Length: > 0 })
        {
            args.Add("--allowedTools");
            foreach (var tool in permissions.AllowedTools)
                args.Add(tool);
        }

        if (permissions.DisallowedTools is { Length: > 0 })
        {
            args.Add("--disallowedTools");
            foreach (var tool in permissions.DisallowedTools)
                args.Add(tool);
        }

        return args;
    }
}
