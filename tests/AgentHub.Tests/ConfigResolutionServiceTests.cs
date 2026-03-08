using System.Text.Json;
using AgentHub.Orchestration.Config;
using Xunit;

namespace AgentHub.Tests;

public class ConfigResolutionServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ConfigResolutionService _sut;

    public ConfigResolutionServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"config-resolution-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        _sut = new ConfigResolutionService(new ConfigLoader(), new ConfigScopeMerger());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void Resolve_NoConfigFiles_ReturnsDefaultScopedPolicyConfig()
    {
        var result = _sut.Resolve("policy", _tempRoot);

        Assert.NotNull(result);
        Assert.Equal(string.Empty, result.Name);
        Assert.Equal("default", result.Scope);
        Assert.Empty(result.TrustTiers);
    }

    [Fact]
    public void Resolve_SingleJsonConfigFile_ReturnsParsedConfig()
    {
        var defaultsDir = Path.Combine(_tempRoot, "defaults");
        Directory.CreateDirectory(defaultsDir);

        var config = new
        {
            name = "test-policy",
            scope = "default",
            skipPermissionPrompts = true,
            enabledSkills = new[] { "Read", "Write" }
        };
        File.WriteAllText(Path.Combine(defaultsDir, "policy.json"),
            JsonSerializer.Serialize(config, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        var result = _sut.Resolve("policy", _tempRoot);

        Assert.Equal("test-policy", result.Name);
        Assert.True(result.SkipPermissionPrompts);
        Assert.Equal(["Read", "Write"], result.EnabledSkills);
    }

    [Fact]
    public void Resolve_MultipleScopes_MergesCorrectly()
    {
        // defaults scope
        var defaultsDir = Path.Combine(_tempRoot, "defaults");
        Directory.CreateDirectory(defaultsDir);
        File.WriteAllText(Path.Combine(defaultsDir, "policy.json"),
            JsonSerializer.Serialize(new
            {
                name = "base-policy",
                scope = "default",
                enabledSkills = new[] { "Read" },
                defaultTimeoutSeconds = 30
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        // host scope (overrides name, adds elevated skills)
        var hostDir = Path.Combine(_tempRoot, "hosts", "host1");
        Directory.CreateDirectory(hostDir);
        File.WriteAllText(Path.Combine(hostDir, "policy.json"),
            JsonSerializer.Serialize(new
            {
                name = "host-policy",
                scope = "host",
                scopeId = "host1",
                enabledSkills = new[] { "Read", "Write" },
                elevatedSkills = new[] { "Bash" }
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        var result = _sut.Resolve("policy", _tempRoot, hostId: "host1");

        // Last wins for scalars
        Assert.Equal("host-policy", result.Name);
        Assert.Equal(30, result.DefaultTimeoutSeconds);
        // Last non-empty wins for enabled skills
        Assert.Equal(["Read", "Write"], result.EnabledSkills);
        // Union for elevated skills
        Assert.Contains("Bash", result.ElevatedSkills);
    }

    [Fact]
    public void Resolve_YamlConfig_CorrectlyLoadsAndMerges()
    {
        var defaultsDir = Path.Combine(_tempRoot, "defaults");
        Directory.CreateDirectory(defaultsDir);

        var yamlContent = @"name: yaml-policy
scope: default
skipPermissionPrompts: true
enabledSkills:
  - Read
  - Glob
";
        File.WriteAllText(Path.Combine(defaultsDir, "policy.yaml"), yamlContent);

        var result = _sut.Resolve("policy", _tempRoot);

        Assert.Equal("yaml-policy", result.Name);
        Assert.True(result.SkipPermissionPrompts);
        Assert.Contains("Read", result.EnabledSkills);
        Assert.Contains("Glob", result.EnabledSkills);
    }

    [Fact]
    public void Resolve_MissingScopeDirectories_DoesNotThrow()
    {
        // Only create defaults, request host/project/task that don't exist
        var defaultsDir = Path.Combine(_tempRoot, "defaults");
        Directory.CreateDirectory(defaultsDir);
        File.WriteAllText(Path.Combine(defaultsDir, "policy.json"),
            JsonSerializer.Serialize(new { name = "only-defaults" },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        // These scope dirs don't exist -- should not throw
        var result = _sut.Resolve("policy", _tempRoot,
            hostId: "nonexistent-host",
            projectId: "nonexistent-project",
            taskId: "nonexistent-task");

        Assert.Equal("only-defaults", result.Name);
    }
}
