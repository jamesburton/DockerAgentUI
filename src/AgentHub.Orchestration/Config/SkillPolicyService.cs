using System.Text.Json;
using AgentHub.Contracts;

namespace AgentHub.Orchestration.Config;

public sealed class JsonSkillPolicyService : ISkillPolicyService
{
    private readonly SkillPolicyDocument _doc;

    public JsonSkillPolicyService(string policyPath)
    {
        _doc = Load(policyPath);
    }

    public PolicySnapshot GetPolicySnapshot()
        => new(_doc.Name, DateTimeOffset.UtcNow, _doc.EnabledSkills, _doc.DisabledSkills, _doc.Notes ?? []);

    public bool IsAllowed(string? skillId, SessionSummary session, bool elevated)
    {
        if (string.IsNullOrWhiteSpace(skillId))
            return elevated || !string.Equals(session.Backend, "ssh", StringComparison.OrdinalIgnoreCase);

        if (_doc.DisabledSkills.Contains(skillId, StringComparer.OrdinalIgnoreCase))
            return false;

        if (_doc.ElevatedSkills?.Contains(skillId, StringComparer.OrdinalIgnoreCase) == true)
            return elevated;

        return _doc.EnabledSkills.Contains(skillId, StringComparer.OrdinalIgnoreCase);
    }

    private static SkillPolicyDocument Load(string path)
    {
        if (!File.Exists(path))
            return new SkillPolicyDocument("default-missing", [], []);
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<SkillPolicyDocument>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? new SkillPolicyDocument("default-invalid", [], []);
    }
}
