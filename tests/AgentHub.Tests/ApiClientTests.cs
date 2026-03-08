using System.Net;
using System.Text.Json;
using AgentHub.Cli.Api;
using AgentHub.Contracts;
using Xunit;

namespace AgentHub.Tests;

public class ApiClientTests
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    private static (AgentHubApiClient Client, MockHandler Handler) CreateClient()
    {
        var handler = new MockHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
        return (new AgentHubApiClient(http), handler);
    }

    [Fact]
    public async Task GetSessionsAsync_DeserializesCorrectly()
    {
        var (client, handler) = CreateClient();
        var sessions = new List<SessionSummary>
        {
            new("s1", "user1", SessionState.Running, DateTimeOffset.UtcNow, "ssh", "node1",
                new SessionRequirements())
        };
        handler.SetResponse(JsonSerializer.Serialize(
            new { items = sessions, totalCount = 1 }, s_json));

        var (items, total) = await client.GetSessionsAsync(take: 10);

        Assert.Single(items);
        Assert.Equal(1, total);
        Assert.Equal("s1", items[0].SessionId);
        Assert.Equal(SessionState.Running, items[0].State);
    }

    [Fact]
    public async Task StartSessionAsync_SendsPayloadAndReturnsSessionId()
    {
        var (client, handler) = CreateClient();
        handler.SetResponse(JsonSerializer.Serialize(new { sessionId = "new-123" }, s_json));

        var req = new StartSessionRequest("claude", new SessionRequirements(), Prompt: "fix bug");
        var sessionId = await client.StartSessionAsync(req);

        Assert.Equal("new-123", sessionId);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/api/sessions", handler.LastRequest.RequestUri!.AbsolutePath);

        var body = await handler.LastRequest.Content!.ReadAsStringAsync();
        Assert.Contains("claude", body);
        Assert.Contains("fix bug", body);
    }

    [Fact]
    public async Task StopSessionAsync_SendsDeleteWithForceFlag()
    {
        var (client, handler) = CreateClient();
        handler.SetResponse("", HttpStatusCode.NoContent);

        await client.StopSessionAsync("abc-123", force: true);

        Assert.Equal(HttpMethod.Delete, handler.LastRequest!.Method);
        Assert.Contains("abc-123", handler.LastRequest.RequestUri!.ToString());
        Assert.Contains("force=true", handler.LastRequest.RequestUri.Query);
    }

    [Fact]
    public async Task GetHostsAsync_DeserializesHostRecordList()
    {
        var (client, handler) = CreateClient();
        var hosts = new List<HostRecord>
        {
            new("h1", "Dev Box", "ssh", "linux", true, true,
                CpuPercent: 45.2, MemUsedMb: 2048, MemTotalMb: 8192),
            new("h2", "Prod Server", "ssh", "linux", true, false)
        };
        handler.SetResponse(JsonSerializer.Serialize(hosts, s_json));

        var result = await client.GetHostsAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("Dev Box", result[0].DisplayName);
        Assert.Equal(45.2, result[0].CpuPercent);
        Assert.Null(result[1].CpuPercent);
    }

    [Fact]
    public async Task GetSessionAsync_ReturnsNullOn404()
    {
        var (client, handler) = CreateClient();
        handler.SetResponse("", HttpStatusCode.NotFound);

        var result = await client.GetSessionAsync("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveApprovalAsync_PostsCorrectPayload()
    {
        var (client, handler) = CreateClient();
        handler.SetResponse("", HttpStatusCode.NoContent);

        await client.ResolveApprovalAsync("appr-1", approved: true);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Contains("appr-1", handler.LastRequest.RequestUri!.ToString());
        var body = await handler.LastRequest.Content!.ReadAsStringAsync();
        Assert.Contains("\"approved\":true", body);
    }

    /// <summary>
    /// Simple mock handler that records the last request and returns a canned response.
    /// </summary>
    private sealed class MockHandler : HttpMessageHandler
    {
        private string _responseBody = "";
        private HttpStatusCode _statusCode = HttpStatusCode.OK;

        public HttpRequestMessage? LastRequest { get; private set; }

        public void SetResponse(string body, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseBody = body;
            _statusCode = statusCode;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Clone the content so it can be read later in assertions
            LastRequest = new HttpRequestMessage(request.Method, request.RequestUri);
            if (request.Content != null)
            {
                var contentBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                LastRequest.Content = new ByteArrayContent(contentBytes);
                LastRequest.Content.Headers.ContentType = request.Content.Headers.ContentType;
            }

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
}
