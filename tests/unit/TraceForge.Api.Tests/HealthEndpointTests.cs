using System.Net;
using System.Text.Json;

namespace TraceForge.Api.Tests;

public sealed class HealthEndpointTests : IClassFixture<TraceForgeApiFactory>
{
    private readonly TraceForgeApiFactory _factory;

    public HealthEndpointTests(TraceForgeApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Liveness_ReturnsHealthy_WhenProcessIsServing()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", document.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Readiness_ReportsExecutedDependencyChecks()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", document.RootElement.GetProperty("status").GetString());

        var checks = document.RootElement.GetProperty("checks").EnumerateArray().ToArray();
        var names = checks.Select(c => c.GetProperty("name").GetString()).ToArray();

        Assert.Contains("log-directory", names);
        Assert.Contains("database", names);
        Assert.All(checks, check => Assert.Equal("Healthy", check.GetProperty("status").GetString()));
    }
}
