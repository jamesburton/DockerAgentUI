using System.Net.ServerSentEvents;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using AgentHub.Contracts;

namespace AgentHub.Web.Services;

public sealed class SseStreamService(IHttpClientFactory httpFactory)
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public ChannelReader<SessionEvent> SubscribeSession(string sessionId, CancellationToken ct = default)
    {
        var channel = Channel.CreateUnbounded<SessionEvent>();
        _ = Task.Run(() => ReadSseAsync(
            $"/api/sessions/{Uri.EscapeDataString(sessionId)}/events", channel.Writer, ct), ct);
        return channel.Reader;
    }

    public ChannelReader<SessionEvent> SubscribeFleet(CancellationToken ct = default)
    {
        var channel = Channel.CreateUnbounded<SessionEvent>();
        _ = Task.Run(() => ReadSseAsync("/api/events", channel.Writer, ct), ct);
        return channel.Reader;
    }

    private async Task ReadSseAsync(string url, ChannelWriter<SessionEvent> writer, CancellationToken ct)
    {
        try
        {
            using var client = httpFactory.CreateClient("SseClient");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            await foreach (var item in SseParser.Create(stream).EnumerateAsync(ct))
            {
                var data = item.Data.ToString();
                if (string.IsNullOrWhiteSpace(data)) continue;

                var evt = JsonSerializer.Deserialize<SessionEvent>(data, s_json);
                if (evt is not null)
                {
                    await writer.WriteAsync(evt, ct);
                }
            }
        }
        catch (OperationCanceledException) { /* expected on shutdown */ }
        catch (Exception) { /* stream ended or errored */ }
        finally
        {
            writer.TryComplete();
        }
    }
}
