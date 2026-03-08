namespace AgentHub.Orchestration.Config;

public sealed record SkillPolicyDocument(
    string Name,
    string[] EnabledSkills,
    string[] DisabledSkills,
    string[]? ElevatedSkills = null,
    string[]? Notes = null);
