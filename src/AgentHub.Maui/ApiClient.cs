using System.Net.Http.Json;
using AgentHub.Contracts;

namespace AgentHub.Maui;

public sealed class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<SessionSummary>> GetSessionsAsync(CancellationToken ct = default)
        => await _http.GetFromJsonAsync<List<SessionSummary>>("/api/sessions", ct) ?? [];

    public async Task<IReadOnlyList<HostRecord>> GetHostsAsync(CancellationToken ct = default)
        => await _http.GetFromJsonAsync<List<HostRecord>>("/api/hosts", ct) ?? [];

    public async Task<IReadOnlyList<SkillManifest>> GetSkillsAsync(CancellationToken ct = default)
        => await _http.GetFromJsonAsync<List<SkillManifest>>("/api/skills", ct) ?? [];
}
