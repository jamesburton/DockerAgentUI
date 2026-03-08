using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentHub.Contracts;
using AgentHub.Orchestration;
using AgentHub.Orchestration.Backends;
using AgentHub.Orchestration.Coordinator;
using AgentHub.Orchestration.Data;
using AgentHub.Orchestration.Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgentHub.Tests;

[Trait("Category", "Integration")]
public class SessionHistoryTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _dbPath;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public SessionHistoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"agenthub-history-test-{Guid.NewGuid():N}.db");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:AgentHub",
                $"Data Source={_dbPath};Cache=Shared");
        });

        _client = _factory.CreateClient();
    }

    // --- GET /api/sessions pagination ---

    [Fact]
    public async Task GetSessions_ReturnsPaginatedList()
    {
        // Seed sessions
        await SeedSessions(5, "dev-user");

        var response = await _client.GetAsync("/api/sessions?skip=0&take=2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        var totalCount = json.GetProperty("totalCount").GetInt32();

        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal(5, totalCount);
    }

    [Fact]
    public async Task GetSessions_WithStateFilter_ReturnsOnlyMatchingState()
    {
        // Seed mixed sessions
        await SeedSessions(3, "dev-user", SessionState.Running);
        await SeedSessions(2, "dev-user", SessionState.Stopped);

        var response = await _client.GetAsync("/api/sessions?state=Stopped");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var totalCount = json.GetProperty("totalCount").GetInt32();

        Assert.Equal(2, totalCount);
    }

    // --- GET /api/sessions/{id}/history ---

    [Fact]
    public async Task GetSessionHistory_ReturnsEventsOrderedByTimestamp()
    {
        var sessionId = await SeedSessionWithEvents("dev-user", 3);

        // Also seed the session in InMemoryBackend so coordinator.GetSessionAsync finds it
        await SeedSessionInBackend(sessionId);

        var response = await _client.GetAsync($"/api/sessions/{sessionId}/history");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var events = await response.Content.ReadFromJsonAsync<JsonElement[]>(JsonOpts);
        Assert.NotNull(events);
        Assert.Equal(3, events.Length);

        // Verify ordered by timestamp ascending
        for (int i = 1; i < events.Length; i++)
        {
            var prev = events[i - 1].GetProperty("tsUtc").GetDateTimeOffset();
            var curr = events[i].GetProperty("tsUtc").GetDateTimeOffset();
            Assert.True(curr >= prev, "Events should be ordered by timestamp ascending");
        }
    }

    [Fact]
    public async Task GetSessionHistory_NonExistentSession_Returns404()
    {
        var response = await _client.GetAsync("/api/sessions/nonexistent/history");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- DELETE /api/sessions/{id} ---

    [Fact]
    public async Task DeleteSession_WithForceTrue_SendsForceKill()
    {
        var sessionId = await SeedSessionInBackendAndDb("dev-user");

        var response = await _client.DeleteAsync($"/api/sessions/{sessionId}?force=true");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task DeleteSession_WithoutForce_SendsGracefulStop()
    {
        var sessionId = await SeedSessionInBackendAndDb("dev-user");

        var response = await _client.DeleteAsync($"/api/sessions/{sessionId}");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    // --- POST /api/approvals/{id}/resolve ---

    [Fact]
    public async Task ResolveApproval_WithApprovedTrue_Returns200()
    {
        // Get the approval service and create a pending approval
        var approvalService = _factory.Services.GetRequiredService<ApprovalService>();

        // Start a pending approval in the background
        var sessionId = await SeedSessionInBackendAndDb("dev-user");
        var approvalTask = Task.Run(async () =>
        {
            var decision = await approvalService.RequestApprovalAsync(
                sessionId,
                new ApprovalContext("test-action", "rm -rf /", null, null, 30, "stop", false),
                _ => Task.CompletedTask,
                CancellationToken.None);
            return decision;
        });

        // Wait for the approval to be pending
        await Task.Delay(200);

        var pendingIds = approvalService.GetPendingApprovalIds();
        Assert.NotEmpty(pendingIds);

        var approvalId = pendingIds[0];
        var body = JsonContent.Create(new { approved = true, resolvedBy = "test-user" });
        var response = await _client.PostAsync($"/api/approvals/{approvalId}/resolve", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify the approval was resolved
        var decision = await approvalTask;
        Assert.Equal(ApprovalDecision.Approved, decision);
    }

    [Fact]
    public async Task ResolveApproval_WithApprovedFalse_DeniesApproval()
    {
        var approvalService = _factory.Services.GetRequiredService<ApprovalService>();

        var sessionId = await SeedSessionInBackendAndDb("dev-user");
        var approvalTask = Task.Run(async () =>
        {
            return await approvalService.RequestApprovalAsync(
                sessionId,
                new ApprovalContext("delete-file", "rm file.txt", null, null, 30, "stop", false),
                _ => Task.CompletedTask,
                CancellationToken.None);
        });

        await Task.Delay(200);
        var pendingIds = approvalService.GetPendingApprovalIds();
        Assert.NotEmpty(pendingIds);

        var approvalId = pendingIds[0];
        var body = JsonContent.Create(new { approved = false, resolvedBy = "test-user" });
        var response = await _client.PostAsync($"/api/approvals/{approvalId}/resolve", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var decision = await approvalTask;
        Assert.Equal(ApprovalDecision.Denied, decision);
    }

    // --- Helpers ---

    private async Task SeedSessions(int count, string userId, SessionState? state = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentHubDbContext>();

        for (int i = 0; i < count; i++)
        {
            db.Sessions.Add(new SessionEntity
            {
                SessionId = $"hist-{Guid.NewGuid():N}",
                OwnerUserId = userId,
                State = state ?? (i % 2 == 0 ? SessionState.Running : SessionState.Stopped),
                CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-count + i),
                Backend = "nomad",
                RequirementsJson = "{}"
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task<string> SeedSessionWithEvents(string userId, int eventCount)
    {
        var sessionId = $"hist-{Guid.NewGuid():N}";
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentHubDbContext>();

        db.Sessions.Add(new SessionEntity
        {
            SessionId = sessionId,
            OwnerUserId = userId,
            State = SessionState.Running,
            CreatedUtc = DateTimeOffset.UtcNow,
            Backend = "nomad",
            RequirementsJson = "{}"
        });

        for (int i = 0; i < eventCount; i++)
        {
            db.Events.Add(new SessionEventEntity
            {
                SessionId = sessionId,
                Kind = SessionEventKind.StdOut,
                TsUtc = DateTimeOffset.UtcNow.AddSeconds(i),
                Data = $"output-line-{i}"
            });
        }

        await db.SaveChangesAsync();
        return sessionId;
    }

    private async Task SeedSessionInBackend(string sessionId)
    {
        var backends = _factory.Services.GetServices<ISessionBackend>();
        var inMemory = backends.OfType<InMemoryBackend>().First();

        // Seed via the backend's StartAsync (generates its own ID, but we need our ID in backend)
        // Instead, seed the session in the DB with the nomad backend name so coordinator can find it
        // The coordinator's GetSessionAsync queries backends; InMemoryBackend won't have our DB-seeded session.
        // We need to ensure the session is findable. Use the InMemoryBackend to create one.
        // Actually, let's just start a session and update our DB record to match.
        // Simpler: just ensure the session exists in DB and InMemoryBackend both.

        // Start a session in InMemoryBackend and get its generated ID - then update DB to match
        // But we already have a session in DB. The history endpoint calls GetSessionAsync which
        // iterates backends. InMemoryBackend won't find our DB session. SshBackend might.
        // Let's just add a nomad-named entry via InMemoryBackend and seed DB for that session.
        // Actually, the SshBackend queries DB directly via GetAsync, so sessions in DB with backend="ssh" will work.
        // Let's update the seeded session to use backend="ssh" instead.

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentHubDbContext>();
        var entity = await db.Sessions.FindAsync(sessionId);
        if (entity != null)
        {
            entity.Backend = "ssh";
            await db.SaveChangesAsync();
        }
    }

    private async Task<string> SeedSessionInBackendAndDb(string userId)
    {
        // Create session via InMemoryBackend so coordinator can find it
        var backends = _factory.Services.GetServices<ISessionBackend>();
        var inMemory = backends.OfType<InMemoryBackend>().First();

        var placement = new PlacementDecision("nomad", "nomad-dev-01");
        var req = new StartSessionRequest("test-image", new SessionRequirements());
        var sessionId = await inMemory.StartAsync(userId, req, placement, _ => Task.CompletedTask, CancellationToken.None);

        // Also seed in DB for FK/history queries
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentHubDbContext>();
        if (!await db.Sessions.AnyAsync(s => s.SessionId == sessionId))
        {
            db.Sessions.Add(new SessionEntity
            {
                SessionId = sessionId,
                OwnerUserId = userId,
                State = SessionState.Running,
                CreatedUtc = DateTimeOffset.UtcNow,
                Backend = "nomad",
                RequirementsJson = "{}"
            });
            await db.SaveChangesAsync();
        }

        return sessionId;
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
