extern alias WebApp;
using System.Net;
using System.Text;
using System.Text.Json;
using AgentHub.Contracts;
using SseStreamService = WebApp::AgentHub.Web.Services.SseStreamService;
using Xunit;

namespace AgentHub.Tests;

public sealed class SseStreamServiceTests
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    private sealed class MockSseHandler : HttpMessageHandler
    {
        public string SseContent { get; set; } = "";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(SseContent));
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(stream)
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
            return Task.FromResult(response);
        }
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public TestHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) =>
            new(_handler, disposeHandler: false) { BaseAddress = new Uri("http://localhost") };
    }

    private static string BuildSsePayload(params SessionEvent[] events)
    {
        var sb = new StringBuilder();
        foreach (var evt in events)
        {
            sb.Append("data: ");
            sb.AppendLine(JsonSerializer.Serialize(evt, s_json));
            sb.AppendLine(); // blank line separates events
        }
        return sb.ToString();
    }

    [Fact]
    public async Task SubscribeSession_ReadsEvents_WritesToChannel()
    {
        var evt1 = new SessionEvent("s1", SessionEventKind.StdOut, DateTimeOffset.UtcNow, "hello");
        var evt2 = new SessionEvent("s1", SessionEventKind.StdErr, DateTimeOffset.UtcNow, "error");

        var handler = new MockSseHandler { SseContent = BuildSsePayload(evt1, evt2) };
        var factory = new TestHttpClientFactory(handler);
        var service = new SseStreamService(factory);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var reader = service.SubscribeSession("s1", cts.Token);

        var received = new List<SessionEvent>();
        await foreach (var e in reader.ReadAllAsync(cts.Token))
        {
            received.Add(e);
        }

        Assert.Equal(2, received.Count);
        Assert.Equal("hello", received[0].Data);
        Assert.Equal(SessionEventKind.StdOut, received[0].Kind);
        Assert.Equal("error", received[1].Data);
        Assert.Equal(SessionEventKind.StdErr, received[1].Kind);
    }

    [Fact]
    public async Task SubscribeSession_CompletesChannel_WhenStreamEnds()
    {
        var evt = new SessionEvent("s1", SessionEventKind.Info, DateTimeOffset.UtcNow, "done");
        var handler = new MockSseHandler { SseContent = BuildSsePayload(evt) };
        var factory = new TestHttpClientFactory(handler);
        var service = new SseStreamService(factory);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var reader = service.SubscribeSession("s1", cts.Token);

        var received = new List<SessionEvent>();
        await foreach (var e in reader.ReadAllAsync(cts.Token))
        {
            received.Add(e);
        }

        // If we get here, the channel completed (ReadAllAsync finished)
        Assert.Single(received);
        Assert.True(reader.Completion.IsCompleted);
    }

    [Fact]
    public async Task SubscribeFleet_ReadsFleetEvents()
    {
        var evt = new SessionEvent("fleet1", SessionEventKind.Metric, DateTimeOffset.UtcNow, "cpu=42");
        var handler = new MockSseHandler { SseContent = BuildSsePayload(evt) };
        var factory = new TestHttpClientFactory(handler);
        var service = new SseStreamService(factory);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var reader = service.SubscribeFleet(cts.Token);

        var received = new List<SessionEvent>();
        await foreach (var e in reader.ReadAllAsync(cts.Token))
        {
            received.Add(e);
        }

        Assert.Single(received);
        Assert.Equal("fleet1", received[0].SessionId);
        Assert.Equal("cpu=42", received[0].Data);
    }
}
