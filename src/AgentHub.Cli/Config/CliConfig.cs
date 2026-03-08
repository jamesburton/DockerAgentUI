using System.Text.Json;

namespace AgentHub.Cli.Config;

public sealed class CliConfig
{
    public string ServerUrl { get; set; } = "http://localhost:5000";
    public string? DefaultHost { get; set; }
    public string DefaultAgent { get; set; } = "claude";
    public int WatchRefreshMs { get; set; } = 2000;
    public int LogTailDefault { get; set; } = 100;

    private static readonly JsonSerializerOptions s_options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".agenthub");

    public static string ConfigPath => Path.Combine(ConfigDir, "config.json");

    public static CliConfig Load()
    {
        var path = ConfigPath;
        if (!File.Exists(path))
            return new CliConfig();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<CliConfig>(json, s_options) ?? new CliConfig();
        }
        catch
        {
            return new CliConfig();
        }
    }

    public void Save()
    {
        var dir = ConfigDir;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(this, s_options);
        File.WriteAllText(ConfigPath, json);
    }
}
