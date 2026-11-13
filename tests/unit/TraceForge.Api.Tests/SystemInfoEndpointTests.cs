using System.Net;
using System.Text.Json;

namespace TraceForge.Api.Tests;

public sealed class SystemInfoEndpointTests : IClassFixture<TraceForgeApiFactory>
{
    private readonly TraceForgeApiFactory _factory;

    public SystemInfoEndpointTests(TraceForgeApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ReturnsRealIdentityAndUptime()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/system/info", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal("TraceForge AI", root.GetProperty("application").GetString());
        Assert.Equal("TraceForge AI", root.GetProperty("instanceName").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("version").GetString()));
        Assert.True(root.GetProperty("uptimeSeconds").GetDouble() >= 0);

        var serverTime = root.GetProperty("serverTimeUtc").GetDateTimeOffset();
        var startedAt = root.GetProperty("startedAtUtc").GetDateTimeOffset();
        Assert.True(serverTime >= startedAt);
        Assert.True((DateTimeOffset.UtcNow - serverTime).Duration() < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task InstanceName_ReflectsConfiguration()
    {
        using var factory = TraceForgeApiFactory.WithConfiguration(
            ("TraceForge:InstanceName", "Lab Instance"));
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/system/info", UriKind.Relative));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal("Lab Instance", document.RootElement.GetProperty("instanceName").GetString());
    }
}
