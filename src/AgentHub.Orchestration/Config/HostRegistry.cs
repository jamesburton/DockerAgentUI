using System.Text.Json;
using AgentHub.Contracts;

namespace AgentHub.Orchestration.Config;

public sealed class JsonHostRegistry : IHostRegistry
{
    private readonly string _path;
    private readonly Lazy<IReadOnlyList<HostRecord>> _hosts;

    public JsonHostRegistry(string path)
    {
        _path = path;
        _hosts = new Lazy<IReadOnlyList<HostRecord>>(Load);
    }

    public Task<IReadOnlyList<HostRecord>> ListAsync(CancellationToken ct)
        => Task.FromResult(_hosts.Value);

    public Task<HostRecord?> GetAsync(string hostId, CancellationToken ct)
        => Task.FromResult(_hosts.Value.FirstOrDefault(x => string.Equals(x.HostId, hostId, StringComparison.OrdinalIgnoreCase)));

    private IReadOnlyList<HostRecord> Load()
    {
        if (!File.Exists(_path))
            return Array.Empty<HostRecord>();

        var json = File.ReadAllText(_path);
        return JsonSerializer.Deserialize<List<HostRecord>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? [];
    }
}
