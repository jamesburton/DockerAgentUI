using System.Net;
using System.Net.Http.Json;
using AgentHub.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AgentHub.Tests;

[Trait("Category", "Integration")]
public class ApiEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:AgentHub", "Data Source=:memory:");
        }).CreateClient();
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

        var hosts = await response.Content.ReadFromJsonAsync<List<HostRecord>>();
        Assert.NotNull(hosts);
        // After seeding from hosts.json, should have hosts
        Assert.NotEmpty(hosts);
    }

    [Fact]
    public async Task GetSessions_Returns200WithEmptyListInitially()
    {
        var response = await _client.GetAsync("/api/sessions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var sessions = await response.Content.ReadFromJsonAsync<List<SessionSummary>>();
        Assert.NotNull(sessions);
        Assert.Empty(sessions);
    }

    private record HealthResponse(bool Ok);
}
