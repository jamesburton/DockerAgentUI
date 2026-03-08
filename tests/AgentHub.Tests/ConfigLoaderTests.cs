using AgentHub.Orchestration.Config;
using Xunit;

namespace AgentHub.Tests;

public class ConfigLoaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigLoader _loader;

    public ConfigLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ConfigLoaderTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _loader = new ConfigLoader();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, content);
        return path;
    }

    // --- ConfigLoader JSON ---

    [Fact]
    public void Load_Json_Deserializes_To_Typed_Object()
    {
        var path = WriteFile("policy.json", """
        {
            "name": "test-json",
            "scope": "host",
            "enabledSkills": ["code-review"],
            "defaultTimeoutSeconds": 45
        }
        """);

        var result = _loader.Load<ScopedPolicyConfig>(path);

        Assert.Equal("test-json", result.Name);
        Assert.Equal("host", result.Scope);
        Assert.Single(result.EnabledSkills);
        Assert.Equal("code-review", result.EnabledSkills[0]);
        Assert.Equal(45, result.DefaultTimeoutSeconds);
    }

    // --- ConfigLoader YAML (.yaml) ---

    [Fact]
    public void Load_Yaml_Deserializes_To_Typed_Object()
    {
        var path = WriteFile("policy.yaml", """
        name: test-yaml
        scope: project
        enabledSkills:
          - code-review
          - test-gen
        defaultTimeoutSeconds: 60
        """);

        var result = _loader.Load<ScopedPolicyConfig>(path);

        Assert.Equal("test-yaml", result.Name);
        Assert.Equal("project", result.Scope);
        Assert.Equal(2, result.EnabledSkills.Length);
        Assert.Equal(60, result.DefaultTimeoutSeconds);
    }

    // --- ConfigLoader YAML (.yml) ---

    [Fact]
    public void Load_Yml_Deserializes_To_Typed_Object()
    {
        var path = WriteFile("policy.yml", """
        name: test-yml
        scope: task
        disabledSkills:
          - deploy
        """);

        var result = _loader.Load<ScopedPolicyConfig>(path);

        Assert.Equal("test-yml", result.Name);
        Assert.Equal("task", result.Scope);
        Assert.Single(result.DisabledSkills);
        Assert.Equal("deploy", result.DisabledSkills[0]);
    }

    // --- ConfigLoader Markdown frontmatter ---

    [Fact]
    public void Load_Markdown_Extracts_Yaml_Frontmatter()
    {
        var path = WriteFile("policy.md", """
        ---
        name: test-md
        scope: project
        enabledSkills:
          - analysis
        defaultTimeoutSeconds: 120
        ---

        # Policy Instructions

        These are the instructions for the agent.
        Follow these rules carefully.
        """);

        var result = _loader.Load<ScopedPolicyConfig>(path);

        Assert.Equal("test-md", result.Name);
        Assert.Equal("project", result.Scope);
        Assert.Single(result.EnabledSkills);
        Assert.Equal(120, result.DefaultTimeoutSeconds);
    }

    // --- ConfigLoader Markdown preserves body as Instructions ---

    [Fact]
    public void Load_Markdown_Preserves_Body_As_Instructions()
    {
        var path = WriteFile("policy-with-body.md", """
        ---
        name: test-instructions
        scope: default
        ---

        # Important Rules

        1. Always write tests first
        2. Never delete production data
        """);

        var result = _loader.Load<ScopedPolicyConfig>(path);

        Assert.Equal("test-instructions", result.Name);
        Assert.NotNull(result.Instructions);
        Assert.Contains("Important Rules", result.Instructions);
        Assert.Contains("Always write tests first", result.Instructions);
    }

    // --- ConfigLoader unsupported format ---

    [Fact]
    public void Load_Unsupported_Format_Throws_NotSupportedException()
    {
        var path = WriteFile("policy.txt", "some content");

        Assert.Throws<NotSupportedException>(() => _loader.Load<ScopedPolicyConfig>(path));
    }

    // --- ConfigLoader LoadFromString ---

    [Fact]
    public void LoadFromString_Json_Works()
    {
        var json = """{"name":"from-string","scope":"default"}""";
        var result = _loader.LoadFromString<ScopedPolicyConfig>(json, "json");

        Assert.Equal("from-string", result.Name);
    }

    [Fact]
    public void LoadFromString_Yaml_Works()
    {
        var yaml = "name: from-yaml-string\nscope: host\n";
        var result = _loader.LoadFromString<ScopedPolicyConfig>(yaml, "yaml");

        Assert.Equal("from-yaml-string", result.Name);
    }

    // --- ConfigScopeMerger scalar last-wins ---

    [Fact]
    public void ConfigScopeMerger_Scalars_Last_NonNull_Wins()
    {
        var merger = new ConfigScopeMerger();
        var configs = new List<ScopedPolicyConfig>
        {
            new() { Name = "default-policy", Scope = "default", DefaultTimeoutSeconds = 30, DefaultTimeoutAction = "stop" },
            new() { Name = "host-policy", Scope = "host", DefaultTimeoutSeconds = 60 }
        };

        var result = merger.Merge(configs);

        Assert.Equal("host-policy", result.Name);
        Assert.Equal(60, result.DefaultTimeoutSeconds);
        Assert.Equal("stop", result.DefaultTimeoutAction); // kept from default since host is null
    }

    // --- ConfigScopeMerger tool lists last-wins ---

    [Fact]
    public void ConfigScopeMerger_EnabledSkills_LastWins()
    {
        var merger = new ConfigScopeMerger();
        var configs = new List<ScopedPolicyConfig>
        {
            new() { Name = "default", Scope = "default", EnabledSkills = ["code-review", "test-gen", "deploy"] },
            new() { Name = "host", Scope = "host", EnabledSkills = ["code-review", "test-gen"] }
        };

        var result = merger.Merge(configs);

        Assert.Equal(2, result.EnabledSkills.Length);
        Assert.Contains("code-review", result.EnabledSkills);
        Assert.Contains("test-gen", result.EnabledSkills);
        Assert.DoesNotContain("deploy", result.EnabledSkills);
    }

    // --- ConfigScopeMerger deny-lists union ---

    [Fact]
    public void ConfigScopeMerger_DisallowedTools_Union()
    {
        var merger = new ConfigScopeMerger();
        var configs = new List<ScopedPolicyConfig>
        {
            new() { Name = "default", Scope = "default", DisallowedTools = ["rm", "shutdown"] },
            new() { Name = "host", Scope = "host", DisallowedTools = ["format", "rm"] }
        };

        var result = merger.Merge(configs);

        Assert.NotNull(result.DisallowedTools);
        Assert.Equal(3, result.DisallowedTools!.Length);
        Assert.Contains("rm", result.DisallowedTools);
        Assert.Contains("shutdown", result.DisallowedTools);
        Assert.Contains("format", result.DisallowedTools);
    }

    // --- ConfigScopeMerger SkipPermissionPrompts OR logic ---

    [Fact]
    public void ConfigScopeMerger_SkipPermissionPrompts_OR_Logic()
    {
        var merger = new ConfigScopeMerger();
        var configs = new List<ScopedPolicyConfig>
        {
            new() { Name = "default", Scope = "default", SkipPermissionPrompts = false },
            new() { Name = "host", Scope = "host", SkipPermissionPrompts = true }
        };

        var result = merger.Merge(configs);

        Assert.True(result.SkipPermissionPrompts);
    }

    // --- ConfigScopeMerger 4-scope hierarchy ---

    [Fact]
    public void ConfigScopeMerger_FourScope_Hierarchy_TaskWins()
    {
        var merger = new ConfigScopeMerger();
        var configs = new List<ScopedPolicyConfig>
        {
            new() { Name = "default", Scope = "default", DefaultTimeoutSeconds = 30, EnabledSkills = ["a", "b"] },
            new() { Name = "host", Scope = "host", DefaultTimeoutSeconds = 60, EnabledSkills = ["a", "b", "c"] },
            new() { Name = "project", Scope = "project", DefaultTimeoutSeconds = 90 },
            new() { Name = "task", Scope = "task", DefaultTimeoutSeconds = 120, EnabledSkills = ["x"] }
        };

        var result = merger.Merge(configs);

        Assert.Equal("task", result.Name);
        Assert.Equal(120, result.DefaultTimeoutSeconds);
        Assert.Single(result.EnabledSkills);
        Assert.Equal("x", result.EnabledSkills[0]);
    }

    // --- ConfigScopeMerger TrustTiers merge last-wins per key ---

    [Fact]
    public void ConfigScopeMerger_TrustTiers_LastWins_PerKey()
    {
        var merger = new ConfigScopeMerger();
        var configs = new List<ScopedPolicyConfig>
        {
            new()
            {
                Name = "default", Scope = "default",
                TrustTiers = new() { ["Read"] = TrustTier.AlwaysAllow, ["Bash"] = TrustTier.Prompt }
            },
            new()
            {
                Name = "project", Scope = "project",
                TrustTiers = new() { ["Bash"] = TrustTier.AlwaysAllow, ["Deploy"] = TrustTier.AlwaysDeny }
            }
        };

        var result = merger.Merge(configs);

        Assert.Equal(TrustTier.AlwaysAllow, result.TrustTiers["Read"]);
        Assert.Equal(TrustTier.AlwaysAllow, result.TrustTiers["Bash"]); // overridden
        Assert.Equal(TrustTier.AlwaysDeny, result.TrustTiers["Deploy"]);
    }

    // --- ConfigScopeMerger ElevatedSkills union ---

    [Fact]
    public void ConfigScopeMerger_ElevatedSkills_Union()
    {
        var merger = new ConfigScopeMerger();
        var configs = new List<ScopedPolicyConfig>
        {
            new() { Name = "default", Scope = "default", ElevatedSkills = ["admin-ops"] },
            new() { Name = "host", Scope = "host", ElevatedSkills = ["deploy", "admin-ops"] }
        };

        var result = merger.Merge(configs);

        Assert.Equal(2, result.ElevatedSkills.Length);
        Assert.Contains("admin-ops", result.ElevatedSkills);
        Assert.Contains("deploy", result.ElevatedSkills);
    }

    // --- ConfigDiscovery finds configs across scope directories ---

    [Fact]
    public void ConfigDiscovery_FindConfigFiles_Discovers_Across_Scopes()
    {
        // Create scope directories
        WriteFile("defaults/policy.yaml", "name: default\n");
        WriteFile("hosts/host-1/policy.json", """{"name":"host-1"}""");
        WriteFile("projects/my-project/policy.md", "---\nname: project\n---\n");
        WriteFile("tasks/task-42/policy.yml", "name: task\n");

        var files = ConfigDiscovery.FindConfigFiles("policy", _tempDir, "host-1", "my-project", "task-42");

        Assert.Equal(4, files.Count);
        Assert.Contains(files, f => f.EndsWith("policy.yaml"));
        Assert.Contains(files, f => f.EndsWith("policy.json"));
        Assert.Contains(files, f => f.EndsWith("policy.md"));
        Assert.Contains(files, f => f.EndsWith("policy.yml"));
    }

    [Fact]
    public void ConfigDiscovery_FindConfigFiles_Returns_Empty_When_No_Configs()
    {
        var files = ConfigDiscovery.FindConfigFiles("policy", _tempDir, null, null, null);
        Assert.Empty(files);
    }

    [Fact]
    public void ConfigDiscovery_FindConfigFiles_Partial_Scopes()
    {
        WriteFile("defaults/policy.json", """{"name":"default"}""");
        WriteFile("projects/proj-1/policy.yaml", "name: project\n");

        var files = ConfigDiscovery.FindConfigFiles("policy", _tempDir, null, "proj-1", null);

        Assert.Equal(2, files.Count);
    }

    // --- ConfigScopeMerger DisabledSkills last-wins ---

    [Fact]
    public void ConfigScopeMerger_DisabledSkills_LastWins()
    {
        var merger = new ConfigScopeMerger();
        var configs = new List<ScopedPolicyConfig>
        {
            new() { Name = "default", Scope = "default", DisabledSkills = ["deploy", "admin"] },
            new() { Name = "project", Scope = "project", DisabledSkills = ["deploy"] }
        };

        var result = merger.Merge(configs);

        Assert.Single(result.DisabledSkills);
        Assert.Equal("deploy", result.DisabledSkills[0]);
    }
}
