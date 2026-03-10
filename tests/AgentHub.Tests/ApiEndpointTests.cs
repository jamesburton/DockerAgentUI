using System.Net;
using System.Net.Http.Json;
using AgentHub.Contracts;
using AgentHub.Orchestration.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgentHub.Tests;

[Trait("Category", "Integration")]
public class ApiEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _dbPath;

    public ApiEndpointTests(WebApplicationFactory<Program> factory)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"agenthub-test-{Guid.NewGuid():N}.db");

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:AgentHub",
                $"Data Source={_dbPath};Cache=Shared");
        });

        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task HealthCheck_Returns200WithOkTrue()
    {
        var response = await _client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(body);
        Assert.True(body.Ok);
    }

    [Fact]
    public async Task GetHosts_Returns200WithList()
    {
        var response = await _client.GetAsync("/api/hosts");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var hosts = await response.Content.ReadFromJsonAsync<List<HostRecord>>(
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.NotNull(hosts);
        // hosts.json may be empty; just verify the endpoint returns a valid list
        Assert.NotNull(hosts);
    }

    [Fact]
    public async Task GetSessions_Returns200WithEmptyListInitially()
    {
        var response = await _client.GetAsync("/api/sessions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var items = json.GetProperty("items");
        var totalCount = json.GetProperty("totalCount").GetInt32();
        Assert.Equal(0, items.GetArrayLength());
        Assert.Equal(0, totalCount);
    }

    public void Dispose()
    {
        _client.Dispose();
        try { File.Delete(_dbPath); } catch { }
        try { File.Delete(_dbPath + "-wal"); } catch { }
        try { File.Delete(_dbPath + "-shm"); } catch { }
    }

    private record HealthResponse(bool Ok);
}
