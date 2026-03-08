using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using AgentHub.Contracts;

namespace AgentHub.Cli.Api;

/// <summary>
/// Consumes Server-Sent Events streams from the AgentHub API with automatic reconnection.
/// </summary>
public sealed class SseStreamReader
{
    private readonly HttpClient _http;
    private const int MaxRetries = 10;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    public SseStreamReader(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Stream events for a specific session with automatic reconnection.
    /// </summary>
    public IAsyncEnumerable<(string EventId, SessionEvent Event)> StreamSessionEventsAsync(
        string sessionId, string? lastEventId = null, CancellationToken ct = default)
    {
        var url = $"/api/sessions/{Uri.EscapeDataString(sessionId)}/events";
        return StreamWithReconnectAsync(url, lastEventId, ct);
    }

    /// <summary>
    /// Stream fleet-wide events with automatic reconnection.
    /// </summary>
    public IAsyncEnumerable<(string EventId, SessionEvent Event)> StreamFleetEventsAsync(
        string? lastEventId = null, CancellationToken ct = default)
    {
        return StreamWithReconnectAsync("/api/events", lastEventId, ct);
    }

    private async IAsyncEnumerable<(string EventId, SessionEvent Event)> StreamWithReconnectAsync(
        string url, string? lastEventId, [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<(string EventId, SessionEvent Event)>();

        // Run stream consumption in background, writing to channel
        var producerTask = Task.Run(async () =>
        {
            int retries = 0;
            string? currentLastId = lastEventId;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        var request = new HttpRequestMessage(HttpMethod.Get, url);
                        request.Headers.Accept.ParseAdd("text/event-stream");
                        if (currentLastId is not null)
                            request.Headers.TryAddWithoutValidation("Last-Event-ID", currentLastId);

                        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                        response.EnsureSuccessStatusCode();

                        var stream = await response.Content.ReadAsStreamAsync(ct);

                        // Reset retry count on successful connection
                        retries = 0;

                        await foreach (var sseItem in SseParser.Create(stream).EnumerateAsync(ct))
                        {
                            var data = sseItem.Data.ToString();
                            if (string.IsNullOrWhiteSpace(data))
                                continue;

                            var sessionEvent = JsonSerializer.Deserialize<SessionEvent>(data, s_json);
                            if (sessionEvent is null)
                                continue;

                            var eventId = sseItem.EventId.ToString();
                            if (!string.IsNullOrEmpty(eventId))
                                currentLastId = eventId;

                            await channel.Writer.WriteAsync((eventId, sessionEvent), ct);
                        }

                        // Stream ended normally
                        break;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex) when (ex is IOException or HttpRequestException)
                    {
                        retries++;
                        if (retries > MaxRetries)
                        {
                            channel.Writer.TryComplete(new InvalidOperationException(
                                $"SSE stream reconnection failed after {MaxRetries} retries.", ex));
                            return;
                        }

                        await Console.Error.WriteLineAsync(
                            $"[SSE] Connection lost, reconnecting ({retries}/{MaxRetries})...");
                        await Task.Delay(RetryDelay, ct);
                    }
                }
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, ct);

        // Read from channel
        await foreach (var item in channel.Reader.ReadAllAsync(ct))
        {
            yield return item;
        }

        await producerTask;
    }
}
