using System.Net;

namespace TraceForge.Api.Tests;

public sealed class SecurityHeaderTests : IClassFixture<TraceForgeApiFactory>
{
    private readonly TraceForgeApiFactory _factory;

    public SecurityHeaderTests(TraceForgeApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ApiResponses_CarryDefensiveHeaders()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/system/info", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("no-store", response.Headers.GetValues("Cache-Control").Single());

        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("default-src 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerHeader_IsNotDisclosed()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/system/info", UriKind.Relative));

        Assert.False(response.Headers.Contains("Server"));
    }

    [Fact]
    public async Task HstsHeader_IsAbsentOnPlainHttp()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/system/info", UriKind.Relative));

        // The test server speaks plain HTTP; advertising HSTS here would be a lie.
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }
}
