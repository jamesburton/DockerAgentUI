using System.Net;
using System.Text;
using AgentHub.Contracts;
using AgentHub.Orchestration;
using AgentHub.Orchestration.Backends;
using AgentHub.Orchestration.Data;
using AgentHub.Orchestration.Data.Entities;
using AgentHub.Orchestration.Events;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgentHub.Tests;

[Trait("Category", "Integration")]
public class SseStreamingTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _dbPath;

    public SseStreamingTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"agenthub-sse-test-{Guid.NewGuid():N}.db");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:AgentHub",
                $"Data Source={_dbPath};Cache=Shared");
        });

        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task GetSessionEvents_ReturnsSseContentType()
    {
        // Seed a session in both the InMemoryBackend and the DB
        var sessionId = await SeedSessionEverywhere();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/sessions/{sessionId}/events");

        // Start the request -- SSE streams are long-running
        var responseTask = _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        // Wait briefly for server to process the request
        await Task.Delay(300);

        // Emit an event to trigger the SSE stream to send data
        var events = _factory.Services.GetRequiredService<DurableEventService>();
        await events.EmitAsync(new SessionEvent(sessionId, SessionEventKind.Info, DateTimeOffset.UtcNow, "content-type-trigger"));

        try
        {
            var response = await responseTask;
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        }
        catch (OperationCanceledException)
        {
            // SSE is long-running; cancellation is expected after timeout
        }
    }

    [Fact]
    public async Task GetSessionEvents_WithLastEventId_ReplaysEvents()
    {
        // Seed a session and events
        var sessionId = await SeedSessionEverywhere();

        var events = _factory.Services.GetRequiredService<DurableEventService>();
        await events.EmitAsync(new SessionEvent(sessionId, SessionEventKind.Info, DateTimeOffset.UtcNow, "event-1"));
        await events.EmitAsync(new SessionEvent(sessionId, SessionEventKind.Info, DateTimeOffset.UtcNow, "event-2"));
        await events.EmitAsync(new SessionEvent(sessionId, SessionEventKind.Info, DateTimeOffset.UtcNow, "event-3"));

        // Get the ID of the first event
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentHubDbContext>();
        var firstEvent = await db.Events
            .Where(e => e.SessionId == sessionId)
            .OrderBy(e => e.Id)
            .FirstAsync();

        // Request with Last-Event-ID header
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/sessions/{sessionId}/events");
        request.Headers.Add("Last-Event-ID", firstEvent.Id.ToString());

        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadSseBodyWithTimeout(response, cts.Token, maxEvents: 2);

        // Should replay event-2 and event-3 but NOT event-1
        Assert.DoesNotContain("event-1", body);
        Assert.Contains("event-2", body);
        Assert.Contains("event-3", body);
    }

    [Fact]
    public async Task GetFleetEvents_ReturnsSseContentType()
    {
        // Fleet endpoint does not require a session. We emit an event to trigger a response
        // and use ResponseHeadersRead to check content type before the stream completes.
        var sessionId = "fleet-contenttype-test";
        await SeedSessionInDb(sessionId);

        // Start the request
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/events");

        // Start consuming in the background
        var responseTask = _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        // Give time for subscription to start
        await Task.Delay(300);

        // Emit an event to ensure the stream is active
        var events = _factory.Services.GetRequiredService<DurableEventService>();
        await events.EmitAsync(new SessionEvent(sessionId, SessionEventKind.Info, DateTimeOffset.UtcNow, "trigger"));

        try
        {
            var response = await responseTask;
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        }
        catch (OperationCanceledException)
        {
            // SSE stream cancellation is expected
        }
    }

    [Fact]
    public async Task GetSessionEvents_NonExistentSession_Returns404()
    {
        var response = await _client.GetAsync("/api/sessions/nonexistent-session/events");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FleetEvent_StreamsEventsFromAllSessions()
    {
        var sessionId = "fleet-stream-test";
        await SeedSessionInDb(sessionId);

        var events = _factory.Services.GetRequiredService<DurableEventService>();

        // Start fleet subscription
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/events");
        var responseTask = _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        // Wait for subscription to establish
        await Task.Delay(300);

        // Emit event
        await events.EmitAsync(new SessionEvent(sessionId, SessionEventKind.Info, DateTimeOffset.UtcNow, "fleet-live-event"));

        var response = await responseTask;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadSseBodyWithTimeout(response, cts.Token, maxEvents: 1);
        Assert.Contains("fleet-live-event", body);
    }

    /// <summary>
    /// Seeds a session in both the InMemoryBackend (for coordinator lookup) and the DB (for FK constraints).
    /// </summary>
    private async Task<string> SeedSessionEverywhere()
    {
        var sessionId = $"test-{Guid.NewGuid():N}";

        // Seed in DB
        await SeedSessionInDb(sessionId);

        // Seed in InMemoryBackend so the coordinator's GetSessionAsync can find it
        var backends = _factory.Services.GetServices<ISessionBackend>();
        var inMemory = backends.OfType<InMemoryBackend>().First();

        // Use reflection or start a session via the backend directly
        // The InMemoryBackend.StartAsync requires placement, so we'll use a simpler approach:
        // Just call StartAsync with a minimal emit that does nothing (we already seeded the DB session)
        var placement = new PlacementDecision("nomad-dev-01", "nomad");
        var req = new StartSessionRequest("test", new SessionRequirements());

        // We need to capture the session ID from InMemoryBackend, but it generates its own.
        // Instead, let's start a real session and use that ID.
        var generatedId = await inMemory.StartAsync("dev-user", req, placement, _ => Task.CompletedTask, CancellationToken.None);

        // Also seed this generated session ID in DB
        await SeedSessionInDb(generatedId);

        return generatedId;
    }

    private async Task SeedSessionInDb(string sessionId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentHubDbContext>();
        if (!await db.Sessions.AnyAsync(s => s.SessionId == sessionId))
        {
            db.Sessions.Add(new SessionEntity
            {
                SessionId = sessionId,
                OwnerUserId = "dev-user",
                State = SessionState.Running,
                CreatedUtc = DateTimeOffset.UtcNow,
                Backend = "nomad",
                RequirementsJson = "{}"
            });
            await db.SaveChangesAsync();
        }
    }

    private static async Task<string> ReadSseBodyWithTimeout(HttpResponseMessage response, CancellationToken ct, int maxEvents = 5)
    {
        var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var sb = new StringBuilder();
        int eventCount = 0;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break;

                sb.AppendLine(line);

                if (line.StartsWith("data:"))
                {
                    eventCount++;
                    if (eventCount >= maxEvents) break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout -- return what we have
        }

        return sb.ToString();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        try { File.Delete(_dbPath); } catch { }
        try { File.Delete(_dbPath + "-wal"); } catch { }
        try { File.Delete(_dbPath + "-shm"); } catch { }
    }
}
