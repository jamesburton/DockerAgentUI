extern alias WebApp;
using System.Net;
using System.Text;
using System.Text.Json;
using AgentHub.Contracts;
using DashboardApiClient = WebApp::AgentHub.Web.Services.DashboardApiClient;
using Xunit;

namespace AgentHub.Tests;

public sealed class DashboardApiClientTests
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    private sealed class MockHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public HttpResponseMessage Response { get; set; } = new(HttpStatusCode.OK);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(Response);
        }
    }

    private static (DashboardApiClient client, MockHandler handler) CreateClient()
    {
        var handler = new MockHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var client = new DashboardApiClient(http);
        return (client, handler);
    }

    private static void SetJsonResponse<T>(MockHandler handler, T body, HttpStatusCode status = HttpStatusCode.OK)
    {
        handler.Response = new HttpResponseMessage(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, s_json), Encoding.UTF8, "application/json")
        };
    }

    [Fact]
    public async Task GetSessionsAsync_SendsGetToSessions_DeserializesResponse()
    {
        var (client, handler) = CreateClient();
        var sessions = new List<SessionSummary>
        {
            new("s1", "user1", SessionState.Running, DateTimeOffset.UtcNow, "ssh", "node1",
                new SessionRequirements())
        };
        SetJsonResponse(handler, new { items = sessions, totalCount = 1 });

        var (items, total) = await client.GetSessionsAsync();

        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal("/api/sessions", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Single(items);
        Assert.Equal(1, total);
        Assert.Equal("s1", items[0].SessionId);
    }

    [Fact]
    public async Task GetSessionsAsync_WithFilters_AppendsQueryParameters()
    {
        var (client, handler) = CreateClient();
        SetJsonResponse(handler, new { items = new List<SessionSummary>(), totalCount = 0 });

        await client.GetSessionsAsync(skip: 5, take: 10, state: "Running");

        var query = handler.LastRequest!.RequestUri!.Query;
        Assert.Contains("skip=5", query);
        Assert.Contains("take=10", query);
        Assert.Contains("state=Running", query);
    }

    [Fact]
    public async Task GetSessionAsync_SendsGetToSessionId_ReturnsSessionSummary()
    {
        var (client, handler) = CreateClient();
        var session = new SessionSummary("s1", "user1", SessionState.Running,
            DateTimeOffset.UtcNow, "ssh", "node1", new SessionRequirements());
        SetJsonResponse(handler, session);

        var result = await client.GetSessionAsync("s1");

        Assert.Equal("/api/sessions/s1", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.NotNull(result);
        Assert.Equal("s1", result!.SessionId);
    }

    [Fact]
    public async Task GetSessionAsync_NotFound_ReturnsNull()
    {
        var (client, handler) = CreateClient();
        handler.Response = new HttpResponseMessage(HttpStatusCode.NotFound);

        var result = await client.GetSessionAsync("missing");

        Assert.Null(result);
    }

    [Fact]
    public async Task StartSessionAsync_PostsRequest_ReturnsSessionId()
    {
        var (client, handler) = CreateClient();
        SetJsonResponse(handler, new { sessionId = "new-session-123" });

        var req = new StartSessionRequest("alpine:latest", new SessionRequirements());
        var sessionId = await client.StartSessionAsync(req);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/api/sessions", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal("new-session-123", sessionId);
    }

    [Fact]
    public async Task StopSessionAsync_SendsDelete_DefaultForceIsFalse()
    {
        var (client, handler) = CreateClient();
        handler.Response = new HttpResponseMessage(HttpStatusCode.NoContent);

        await client.StopSessionAsync("s1");

        Assert.Equal(HttpMethod.Delete, handler.LastRequest!.Method);
        Assert.Contains("/api/sessions/s1", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Contains("force=false", handler.LastRequest.RequestUri.Query);
    }

    [Fact]
    public async Task StopSessionAsync_Force_SendsForceTrue()
    {
        var (client, handler) = CreateClient();
        handler.Response = new HttpResponseMessage(HttpStatusCode.NoContent);

        await client.StopSessionAsync("s1", force: true);

        Assert.Contains("force=true", handler.LastRequest!.RequestUri!.Query);
    }

    [Fact]
    public async Task GetHostsAsync_SendsGetToHosts_ReturnsList()
    {
        var (client, handler) = CreateClient();
        var hosts = new List<HostRecord>
        {
            new("h1", "Host 1", "ssh", "linux", true, true)
        };
        SetJsonResponse(handler, hosts);

        var result = await client.GetHostsAsync();

        Assert.Equal("/api/hosts", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Single(result);
        Assert.Equal("h1", result[0].HostId);
    }

    [Fact]
    public async Task GetSessionHistoryAsync_SendsGetWithPagination()
    {
        var (client, handler) = CreateClient();
        var events = new List<SessionEvent>
        {
            new("s1", SessionEventKind.StdOut, DateTimeOffset.UtcNow, "hello")
        };
        SetJsonResponse(handler, new { items = events, totalCount = 1 });

        var (items, total) = await client.GetSessionHistoryAsync("s1", page: 2, pageSize: 50, kind: "StdOut");

        Assert.Contains("/api/sessions/s1/history", handler.LastRequest!.RequestUri!.AbsolutePath);
        var query = handler.LastRequest.RequestUri.Query;
        Assert.Contains("page=2", query);
        Assert.Contains("pageSize=50", query);
        Assert.Contains("kind=StdOut", query);
        Assert.Single(items);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetSessionHistoryAsync_DeserializesEnvelopeWithTotalCount()
    {
        var (client, handler) = CreateClient();
        var events = new List<SessionEvent>
        {
            new("s1", SessionEventKind.StdOut, DateTimeOffset.UtcNow, "line1"),
            new("s1", SessionEventKind.StdOut, DateTimeOffset.UtcNow, "line2"),
            new("s1", SessionEventKind.StdErr, DateTimeOffset.UtcNow, "err1")
        };
        SetJsonResponse(handler, new { items = events, totalCount = 10 });

        var (items, total) = await client.GetSessionHistoryAsync("s1");

        Assert.Equal(3, items.Count);
        Assert.Equal(10, total);
    }

    [Fact]
    public async Task ResolveApprovalAsync_PostsToApprovalEndpoint()
    {
        var (client, handler) = CreateClient();
        handler.Response = new HttpResponseMessage(HttpStatusCode.OK);

        await client.ResolveApprovalAsync("approval-1", true);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Contains("/api/approvals/approval-1/resolve", handler.LastRequest.RequestUri!.AbsolutePath);
    }
}
