using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AgentHub.Orchestration.Config;

/// <summary>
/// Multi-format config loader supporting JSON, YAML (.yaml/.yml), and Markdown (.md) with frontmatter.
/// </summary>
public sealed partial class ConfigLoader
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly IDeserializer s_yamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Load a config file, detecting format by extension.
    /// </summary>
    public T Load<T>(string path) where T : class, new()
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var content = File.ReadAllText(path);
        return LoadContent<T>(content, ext);
    }

    /// <summary>
    /// Load from a string with specified format (json, yaml, yml, md).
    /// </summary>
    public T LoadFromString<T>(string content, string format) where T : class, new()
    {
        var ext = format.StartsWith('.') ? format.ToLowerInvariant() : $".{format.ToLowerInvariant()}";
        return LoadContent<T>(content, ext);
    }

    private T LoadContent<T>(string content, string ext) where T : class, new()
    {
        return ext switch
        {
            ".json" => JsonSerializer.Deserialize<T>(content, s_jsonOptions) ?? new T(),
            ".yaml" or ".yml" => s_yamlDeserializer.Deserialize<T>(content) ?? new T(),
            ".md" => ParseMarkdownFrontmatter<T>(content),
            _ => throw new NotSupportedException($"Config format '{ext}' is not supported. Use .json, .yaml, .yml, or .md")
        };
    }

    private static T ParseMarkdownFrontmatter<T>(string content) where T : class, new()
    {
        var match = FrontmatterRegex().Match(content);
        if (!match.Success)
            return new T();

        var yaml = match.Groups[1].Value;
        var result = s_yamlDeserializer.Deserialize<T>(yaml) ?? new T();

        // Preserve body as Instructions if the type has that property
        var body = content[(match.Index + match.Length)..].Trim();
        if (!string.IsNullOrWhiteSpace(body))
        {
            var instructionsProp = typeof(T).GetProperty("Instructions",
                BindingFlags.Public | BindingFlags.Instance);
            if (instructionsProp is not null && instructionsProp.PropertyType == typeof(string) && instructionsProp.CanWrite)
            {
                instructionsProp.SetValue(result, body);
            }
        }

        return result;
    }

    [GeneratedRegex(@"^---\s*\r?\n(.*?)\r?\n---", RegexOptions.Singleline)]
    private static partial Regex FrontmatterRegex();
}
