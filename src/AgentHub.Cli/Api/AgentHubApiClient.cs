using System.Net.Http.Json;
using System.Text.Json;
using AgentHub.Contracts;

namespace AgentHub.Cli.Api;

public sealed class AgentHubApiClient
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    public AgentHubApiClient(HttpClient http)
    {
        _http = http;
    }

    // -- Sessions --

    public async Task<(List<SessionSummary> Items, int TotalCount)> GetSessionsAsync(
        int? skip = null, int? take = null, string? state = null, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (skip.HasValue) query.Add($"skip={skip.Value}");
        if (take.HasValue) query.Add($"take={take.Value}");
        if (!string.IsNullOrEmpty(state)) query.Add($"state={Uri.EscapeDataString(state)}");

        var url = "/api/sessions" + (query.Count > 0 ? "?" + string.Join("&", query) : "");
        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SessionListResponse>(s_json, ct)
            ?? throw new InvalidOperationException("Failed to deserialize session list response.");
        return (result.Items, result.TotalCount);
    }

    public async Task<SessionSummary?> GetSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/sessions/{Uri.EscapeDataString(sessionId)}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SessionSummary>(s_json, ct);
    }

    public async Task<string> StartSessionAsync(StartSessionRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/sessions", req, s_json, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<StartSessionResponse>(s_json, ct)
            ?? throw new InvalidOperationException("Failed to deserialize start session response.");
        return result.SessionId;
    }

    public async Task StopSessionAsync(string sessionId, bool force = false, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync(
            $"/api/sessions/{Uri.EscapeDataString(sessionId)}?force={force.ToString().ToLowerInvariant()}", ct);
        response.EnsureSuccessStatusCode();
    }

    // -- Input --

    public async Task<bool> SendInputAsync(string sessionId, string text, CancellationToken ct = default, bool isFollowUp = false)
    {
        var response = await _http.PostAsJsonAsync(
            $"/api/sessions/{Uri.EscapeDataString(sessionId)}/input",
            new SendInputRequest(text, IsFollowUp: isFollowUp), s_json, ct);
        response.EnsureSuccessStatusCode();

        try
        {
            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("delivered", out var prop))
                return prop.GetBoolean();
        }
        catch (JsonException)
        {
            // Response body may not be JSON -- treat as unconfirmed
        }

        return false;
    }

    // -- Hosts --

    public async Task<List<HostRecord>> GetHostsAsync(CancellationToken ct = default)
    {
        var response = await _http.GetAsync("/api/hosts", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<HostRecord>>(s_json, ct)
            ?? [];
    }

    // -- Host Inventory --

    public async Task RefreshInventoryAsync(string hostId, CancellationToken ct = default)
        => await _http.PostAsync($"/api/hosts/{Uri.EscapeDataString(hostId)}/refresh-inventory", null, ct);

    public async Task RefreshAllInventoryAsync(CancellationToken ct = default)
        => await _http.PostAsync("/api/hosts/refresh-inventory", null, ct);

    // -- Session History --

    public async Task<(List<SessionEvent> Items, int TotalCount)> GetSessionHistoryAsync(
        string sessionId, int? page = null, int? pageSize = null, string? kind = null, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (page.HasValue) query.Add($"page={page.Value}");
        if (pageSize.HasValue) query.Add($"pageSize={pageSize.Value}");
        if (!string.IsNullOrEmpty(kind)) query.Add($"kind={Uri.EscapeDataString(kind)}");

        var url = $"/api/sessions/{Uri.EscapeDataString(sessionId)}/history"
            + (query.Count > 0 ? "?" + string.Join("&", query) : "");
        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<SessionHistoryResponse>(s_json, ct)
            ?? throw new InvalidOperationException("Failed to deserialize session history response.");
        return (result.Items, result.TotalCount);
    }

    // -- Approvals --

    public async Task ResolveApprovalAsync(string approvalId, bool approved, CancellationToken ct = default)
    {
        var body = new { approved, resolvedBy = "cli" };
        var response = await _http.PostAsJsonAsync(
            $"/api/approvals/{Uri.EscapeDataString(approvalId)}/resolve", body, s_json, ct);
        response.EnsureSuccessStatusCode();
    }

    // -- Internal DTOs --

    internal sealed record SessionListResponse(List<SessionSummary> Items, int TotalCount);
    internal sealed record SessionHistoryResponse(List<SessionEvent> Items, int TotalCount);
    internal sealed record StartSessionResponse(string SessionId);
}
