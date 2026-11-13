using System.Net;
using System.Text.Json;

namespace TraceForge.Api.Tests;

public sealed class RateLimitingTests
{
    [Fact]
    public async Task ApiSurface_Returns429WithProblemDetails_AfterLimitExceeded()
    {
        using var factory = TraceForgeApiFactory.WithConfiguration(
            ("TraceForge:RateLimiting:PermitLimit", "2"),
            ("TraceForge:RateLimiting:WindowSeconds", "60"));
        using var client = factory.CreateClient();

        var responses = new List<HttpResponseMessage>();
        for (var i = 0; i < 4; i++)
        {
            responses.Add(await client.GetAsync(new Uri("/api/system/info", UriKind.Relative)));
        }

        Assert.Equal(2, responses.Count(r => r.StatusCode == HttpStatusCode.OK));

        var rejected = responses.Where(r => r.StatusCode == HttpStatusCode.TooManyRequests).ToArray();
        Assert.Equal(2, rejected.Length);
        Assert.Equal("application/problem+json", rejected[0].Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await rejected[0].Content.ReadAsStringAsync());
        Assert.Equal(429, document.RootElement.GetProperty("status").GetInt32());

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task HealthProbes_AreNotSubjectToApiRateLimit()
    {
        using var factory = TraceForgeApiFactory.WithConfiguration(
            ("TraceForge:RateLimiting:PermitLimit", "1"),
            ("TraceForge:RateLimiting:WindowSeconds", "60"));
        using var client = factory.CreateClient();

        // Exhaust the /api budget first.
        using var warmup = await client.GetAsync(new Uri("/api/system/info", UriKind.Relative));
        using var exhausted = await client.GetAsync(new Uri("/api/system/info", UriKind.Relative));
        Assert.Equal(HttpStatusCode.TooManyRequests, exhausted.StatusCode);

        // A saturated API budget must never make the instance look unhealthy.
        using var liveness = await client.GetAsync(new Uri("/health/live", UriKind.Relative));
        using var readiness = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
    }
}
