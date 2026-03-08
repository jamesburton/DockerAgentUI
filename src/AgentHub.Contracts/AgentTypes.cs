namespace AgentHub.Contracts;

public enum AgentType
{
    ClaudeCode = 0,
    Codex = 1,
    Copilot = 2,
    Gemini = 3,
    OpenCode = 4
}

public sealed record AgentPermissions(
    bool SkipPermissionPrompts = false,
    string[]? AllowedTools = null,
    string[]? DisallowedTools = null);

public sealed record AgentDefinition(
    AgentType AgentType,
    string DisplayName,
    string CliCommand,
    string OutputFormat,
    AgentPermissions DefaultPermissions);
