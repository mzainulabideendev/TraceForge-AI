using System.Net;

namespace TraceForge.Api.Tests;

public sealed class CorrelationIdTests : IClassFixture<TraceForgeApiFactory>
{
    private const string HeaderName = "X-Request-Id";

    private readonly TraceForgeApiFactory _factory;

    public CorrelationIdTests(TraceForgeApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ValidCallerSuppliedId_IsEchoedBack()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/system/info", UriKind.Relative));
        request.Headers.Add(HeaderName, "client-correlation-123");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("client-correlation-123", response.Headers.GetValues(HeaderName).Single());
    }

    [Fact]
    public async Task GeneratedId_IsUsed_WhenCallerSuppliesMalformedValue()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/system/info", UriKind.Relative));
        request.Headers.TryAddWithoutValidation(HeaderName, "bad id with spaces");

        var response = await client.SendAsync(request);

        var resolved = response.Headers.GetValues(HeaderName).Single();
        Assert.NotEqual("bad id with spaces", resolved);
        Assert.True(Guid.TryParseExact(resolved, "N", out _), $"Expected a generated GUID, got '{resolved}'.");
    }

    [Fact]
    public async Task UnknownRoute_ReturnsProblemDetailsReferencingCorrelationId()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/does-not-exist", UriKind.Relative));
        request.Headers.Add(HeaderName, "trace-me-456");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("trace-me-456", response.Headers.GetValues(HeaderName).Single());

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("trace-me-456", body, StringComparison.Ordinal);
    }
}
