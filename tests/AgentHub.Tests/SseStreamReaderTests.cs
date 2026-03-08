using System.Net;
using System.Text;
using System.Text.Json;
using AgentHub.Cli.Api;
using AgentHub.Contracts;
using Xunit;

namespace AgentHub.Tests;

public class SseStreamReaderTests
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task StreamSessionEventsAsync_ParsesEventsCorrectly()
    {
        // Arrange - create SSE-formatted response
        var event1 = new SessionEvent("session-1", SessionEventKind.StdOut, DateTimeOffset.UtcNow, "Hello world");
        var event2 = new SessionEvent("session-1", SessionEventKind.StateChanged, DateTimeOffset.UtcNow, "Running");

        var sseData = BuildSseStream(
            ("1", event1),
            ("2", event2));

        var handler = new MockHttpHandler(sseData);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var reader = new SseStreamReader(httpClient);

        // Act
        var events = new List<(string EventId, SessionEvent Event)>();
        await foreach (var item in reader.StreamSessionEventsAsync("session-1"))
        {
            events.Add(item);
        }

        // Assert
        Assert.Equal(2, events.Count);
        Assert.Equal("1", events[0].EventId);
        Assert.Equal(SessionEventKind.StdOut, events[0].Event.Kind);
        Assert.Equal("Hello world", events[0].Event.Data);
        Assert.Equal("2", events[1].EventId);
        Assert.Equal(SessionEventKind.StateChanged, events[1].Event.Kind);
    }

    [Fact]
    public async Task StreamFleetEventsAsync_ParsesEventsCorrectly()
    {
        // Arrange
        var event1 = new SessionEvent("session-a", SessionEventKind.SessionCompleted, DateTimeOffset.UtcNow, "Done");

        var sseData = BuildSseStream(("10", event1));

        var handler = new MockHttpHandler(sseData);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var reader = new SseStreamReader(httpClient);

        // Act
        var events = new List<(string EventId, SessionEvent Event)>();
        await foreach (var item in reader.StreamFleetEventsAsync())
        {
            events.Add(item);
        }

        // Assert
        Assert.Single(events);
        Assert.Equal("session-a", events[0].Event.SessionId);
        Assert.Equal(SessionEventKind.SessionCompleted, events[0].Event.Kind);
    }

    [Fact]
    public async Task StreamSessionEventsAsync_SkipsEmptyData()
    {
        // Arrange - include an event with empty data
        var event1 = new SessionEvent("s1", SessionEventKind.Info, DateTimeOffset.UtcNow, "test");
        var sseText = $"id: 1\ndata: {JsonSerializer.Serialize(event1, s_json)}\n\n" +
                      "id: 2\ndata: \n\n" +  // empty data
                      $"id: 3\ndata: {JsonSerializer.Serialize(event1, s_json)}\n\n";

        var handler = new MockHttpHandler(sseText);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var reader = new SseStreamReader(httpClient);

        // Act
        var events = new List<(string EventId, SessionEvent Event)>();
        await foreach (var item in reader.StreamSessionEventsAsync("s1"))
        {
            events.Add(item);
        }

        // Assert - should skip the empty data event
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public async Task StreamSessionEventsAsync_ReconnectsOnIOException()
    {
        // Arrange - first attempt throws IOException mid-stream, second succeeds
        var event1 = new SessionEvent("s1", SessionEventKind.StdOut, DateTimeOffset.UtcNow, "before-error");
        var event2 = new SessionEvent("s1", SessionEventKind.StdOut, DateTimeOffset.UtcNow, "after-reconnect");

        var firstStream = $"id: 1\ndata: {JsonSerializer.Serialize(event1, s_json)}\n\n";
        var secondStream = $"id: 2\ndata: {JsonSerializer.Serialize(event2, s_json)}\n\n";

        var handler = new FailThenSucceedHandler(firstStream, secondStream);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var reader = new SseStreamReader(httpClient);

        // Act
        var events = new List<(string EventId, SessionEvent Event)>();
        await foreach (var item in reader.StreamSessionEventsAsync("s1"))
        {
            events.Add(item);
        }

        // Assert
        Assert.Equal(2, events.Count);
        Assert.Equal("before-error", events[0].Event.Data);
        Assert.Equal("after-reconnect", events[1].Event.Data);

        // Verify Last-Event-ID was sent on reconnection
        Assert.Equal("1", handler.LastEventIdReceived);
    }

    [Fact]
    public async Task StreamSessionEventsAsync_SendsLastEventIdHeader()
    {
        // Arrange
        var event1 = new SessionEvent("s1", SessionEventKind.Info, DateTimeOffset.UtcNow, "test");
        var sseData = BuildSseStream(("42", event1));

        var handler = new MockHttpHandler(sseData);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        var reader = new SseStreamReader(httpClient);

        // Act - provide initial lastEventId
        var events = new List<(string EventId, SessionEvent Event)>();
        await foreach (var item in reader.StreamSessionEventsAsync("s1", lastEventId: "41"))
        {
            events.Add(item);
        }

        // Assert
        Assert.Equal("41", handler.LastEventIdReceived);
    }

    private static string BuildSseStream(params (string id, SessionEvent evt)[] events)
    {
        var sb = new StringBuilder();
        foreach (var (id, evt) in events)
        {
            sb.AppendLine($"id: {id}");
            sb.AppendLine($"data: {JsonSerializer.Serialize(evt, s_json)}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>
    /// Simple mock HTTP handler that returns an SSE-formatted response.
    /// </summary>
    private sealed class MockHttpHandler : HttpMessageHandler
    {
        private readonly string _sseData;
        public string? LastEventIdReceived { get; private set; }

        public MockHttpHandler(string sseData) => _sseData = sseData;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastEventIdReceived = request.Headers.TryGetValues("Last-Event-ID", out var vals)
                ? vals.FirstOrDefault()
                : null;

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(_sseData)))
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Mock handler that throws IOException on first stream read, then succeeds on second.
    /// </summary>
    private sealed class FailThenSucceedHandler : HttpMessageHandler
    {
        private readonly string _firstStream;
        private readonly string _secondStream;
        private int _requestCount;
        public string? LastEventIdReceived { get; private set; }

        public FailThenSucceedHandler(string firstStream, string secondStream)
        {
            _firstStream = firstStream;
            _secondStream = secondStream;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastEventIdReceived = request.Headers.TryGetValues("Last-Event-ID", out var vals)
                ? vals.FirstOrDefault()
                : null;

            _requestCount++;

            if (_requestCount == 1)
            {
                // Return a stream that will produce one event then throw IOException
                var compositeStream = new FailAfterDataStream(_firstStream);
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(compositeStream)
                };
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
                return Task.FromResult(response);
            }
            else
            {
                // Return normal stream on subsequent requests
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(_secondStream)))
                };
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
                return Task.FromResult(response);
            }
        }
    }

    /// <summary>
    /// Stream that returns data first, then throws IOException on subsequent read.
    /// </summary>
    private sealed class FailAfterDataStream : Stream
    {
        private readonly MemoryStream _inner;
        private bool _dataRead;

        public FailAfterDataStream(string data)
        {
            _inner = new MemoryStream(Encoding.UTF8.GetBytes(data));
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var bytesRead = _inner.Read(buffer, offset, count);
            if (bytesRead == 0)
            {
                if (_dataRead)
                    throw new IOException("Simulated connection loss");
                _dataRead = true;
                throw new IOException("Simulated connection loss");
            }
            _dataRead = true;
            return bytesRead;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => Task.FromResult(Read(buffer, offset, count));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            // Bridge to sync Read
            var array = new byte[buffer.Length];
            var bytesRead = Read(array, 0, array.Length);
            array.AsMemory(0, bytesRead).CopyTo(buffer);
            return ValueTask.FromResult(bytesRead);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
