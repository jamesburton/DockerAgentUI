using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentHub.Contracts;

namespace AgentHub.Web.Services;

public sealed class DashboardApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<(List<SessionSummary> Items, int TotalCount)> GetSessionsAsync(
        int? skip = null, int? take = null, string? state = null, CancellationToken ct = default)
    {
        var qs = new List<string>();
        if (skip.HasValue) qs.Add($"skip={skip.Value}");
        if (take.HasValue) qs.Add($"take={take.Value}");
        if (!string.IsNullOrEmpty(state)) qs.Add($"state={Uri.EscapeDataString(state)}");

        var url = qs.Count > 0 ? $"/api/sessions?{string.Join('&', qs)}" : "/api/sessions";
        var resp = await http.GetFromJsonAsync<SessionListResponse>(url, s_json, ct);
        return (resp?.Items ?? [], resp?.TotalCount ?? 0);
    }

    public async Task<SessionSummary?> GetSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"/api/sessions/{Uri.EscapeDataString(sessionId)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SessionSummary>(s_json, ct);
    }

    public async Task<string> StartSessionAsync(StartSessionRequest req, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("/api/sessions", req, s_json, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<StartSessionResponse>(s_json, ct);
        return result!.SessionId;
    }

    public async Task StopSessionAsync(string sessionId, bool force = false, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync(
            $"/api/sessions/{Uri.EscapeDataString(sessionId)}?force={force.ToString().ToLowerInvariant()}", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<HostRecord>> GetHostsAsync(CancellationToken ct = default)
    {
        return await http.GetFromJsonAsync<List<HostRecord>>("/api/hosts", s_json, ct) ?? [];
    }

    public async Task<(List<SessionEvent> Items, int TotalCount)> GetSessionHistoryAsync(
        string sessionId, int? page = null, int? pageSize = null, string? kind = null,
        CancellationToken ct = default)
    {
        var qs = new List<string>();
        if (page.HasValue) qs.Add($"page={page.Value}");
        if (pageSize.HasValue) qs.Add($"pageSize={pageSize.Value}");
        if (!string.IsNullOrEmpty(kind)) qs.Add($"kind={Uri.EscapeDataString(kind)}");

        var url = $"/api/sessions/{Uri.EscapeDataString(sessionId)}/history";
        if (qs.Count > 0) url += $"?{string.Join('&', qs)}";
        var resp = await http.GetFromJsonAsync<SessionHistoryResponse>(url, s_json, ct);
        return (resp?.Items ?? [], resp?.TotalCount ?? 0);
    }

    public async Task<bool> SendInputAsync(string sessionId, string text, bool isFollowUp = false, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(
            $"/api/sessions/{Uri.EscapeDataString(sessionId)}/input",
            new SendInputRequest(text, IsFollowUp: isFollowUp), s_json, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<SendInputResponse>(s_json, ct);
        return result?.Delivered ?? false;
    }

    internal sealed record SendInputResponse(bool Delivered);

    public async Task ResolveApprovalAsync(string approvalId, bool approved, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync(
            $"/api/approvals/{Uri.EscapeDataString(approvalId)}/resolve",
            new { approved }, s_json, ct);
        response.EnsureSuccessStatusCode();
    }

    // -- Host CRUD --

    public async Task<HostRecord> CreateHostAsync(CreateHostRequest req, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("/api/hosts", req, s_json, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<HostRecord>(s_json, ct))!;
    }

    public async Task DeleteHostAsync(string hostId, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"/api/hosts/{Uri.EscapeDataString(hostId)}", ct);
        response.EnsureSuccessStatusCode();
    }

    // -- Worktree --

    public async Task<DiffStats?> GetSessionDiffAsync(string sessionId, CancellationToken ct = default)
    {
        var response = await http.GetAsync($"/api/sessions/{Uri.EscapeDataString(sessionId)}/diff", ct);
        if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.BadRequest)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DiffStats>(s_json, ct);
    }

    // -- Host Inventory --

    public async Task RefreshInventoryAsync(string hostId, CancellationToken ct = default)
        => await http.PostAsync($"/api/hosts/{Uri.EscapeDataString(hostId)}/refresh-inventory", null, ct);

    public async Task RefreshAllInventoryAsync(CancellationToken ct = default)
        => await http.PostAsync("/api/hosts/refresh-inventory", null, ct);

    internal sealed record SessionListResponse(List<SessionSummary> Items, int TotalCount);
    internal sealed record SessionHistoryResponse(List<SessionEvent> Items, int TotalCount);
    internal sealed record StartSessionResponse(string SessionId);

    public sealed record CreateHostRequest(
        string HostId,
        string DisplayName,
        string Backend,
        string? Os = null,
        bool? AllowSsh = null,
        string? Address = null,
        string? DefaultRepoPath = null,
        Dictionary<string, string>? Labels = null);
}
