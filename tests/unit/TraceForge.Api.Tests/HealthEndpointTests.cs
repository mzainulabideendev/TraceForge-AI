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
    public async Task Readiness_ReportsExecutedLogDirectoryCheck()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", document.RootElement.GetProperty("status").GetString());

        var checks = document.RootElement.GetProperty("checks").EnumerateArray().ToArray();
        Assert.Single(checks);
        Assert.Equal("log-directory", checks[0].GetProperty("name").GetString());
        Assert.Equal("Healthy", checks[0].GetProperty("status").GetString());
    }
}
