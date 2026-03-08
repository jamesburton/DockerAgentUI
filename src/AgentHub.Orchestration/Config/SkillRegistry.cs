using System.Text.Json;
using AgentHub.Contracts;

namespace AgentHub.Orchestration.Config;

public sealed class JsonSkillRegistry : ISkillRegistry
{
    private readonly IReadOnlyList<SkillManifest> _skills;

    public JsonSkillRegistry(string skillsFolder)
    {
        _skills = Load(skillsFolder);
    }

    public IReadOnlyList<SkillManifest> GetAll() => _skills;

    public SkillManifest? TryGet(string skillId)
        => _skills.FirstOrDefault(x => string.Equals(x.Id, skillId, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<SkillManifest> Load(string skillsFolder)
    {
        if (!Directory.Exists(skillsFolder))
            return Array.Empty<SkillManifest>();

        var list = new List<SkillManifest>();
        foreach (var path in Directory.EnumerateFiles(skillsFolder, "*.json", SearchOption.AllDirectories))
        {
            var json = File.ReadAllText(path);
            var skill = JsonSerializer.Deserialize<SkillManifest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (skill is not null)
                list.Add(skill);
        }

        return list
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Last())
            .OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
