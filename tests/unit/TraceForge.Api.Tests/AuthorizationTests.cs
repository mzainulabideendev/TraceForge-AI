using System.Net;

namespace TraceForge.Api.Tests;

public sealed class AuthorizationTests : ApiTestBase
{
    [Fact]
    public async Task AdminEndpoint_WithoutSession_Returns401ProblemDetails()
    {
        using var client = new ApiTestClient(Factory);

        using var response = await client.GetAsync("/api/admin/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await ApiTestClient.ReadJsonAsync(response);
        Assert.Equal(401, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task AdminEndpoint_AsAnalyst_Returns403()
    {
        // The first account of a fresh installation becomes Administrator by design, so seed one
        // before asserting how a subsequent Analyst is treated.
        using (var bootstrap = new ApiTestClient(Factory))
        {
            await bootstrap.RegisterAndAuthenticateAsync("bootstrap-admin@example.test", "Bootstrap Admin");
        }

        using var client = new ApiTestClient(Factory);
        await client.RegisterAndAuthenticateAsync("analyst-rbac@example.test", "Analyst Rbac");

        using var me = await client.GetAsync("/api/auth/me");
        var account = await ApiTestClient.ReadJsonAsync(me);
        Assert.Equal("Analyst", account.GetProperty("roles")[0].GetString());

        using var response = await client.GetAsync("/api/admin/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminEndpoint_AsAdministrator_ReturnsAccounts()
    {
        using var client = new ApiTestClient(Factory);
        await client.RegisterAndAuthenticateAsync("admin-rbac@example.test", "Admin Rbac");

        using var me = await client.GetAsync("/api/auth/me");
        var account = await ApiTestClient.ReadJsonAsync(me);
        Assert.Equal("Administrator", account.GetProperty("roles")[0].GetString());

        using var response = await client.GetAsync("/api/admin/users?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await ApiTestClient.ReadJsonAsync(response);
        Assert.True(page.GetProperty("totalCount").GetInt32() >= 1);
        Assert.True(page.GetProperty("items").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task UnsafeRequest_WithoutAntiforgeryToken_IsRejected_EvenWhenAuthenticated()
    {
        using var client = new ApiTestClient(Factory);
        await client.RegisterAndAuthenticateAsync("csrf@example.test", "Csrf Subject");

        using var response = await client.PostAsync("/api/auth/logout", includeCsrfToken: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // The session must survive the rejected request.
        using var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task HealthProbes_AndSystemInfo_RemainAnonymous()
    {
        using var client = new ApiTestClient(Factory);

        using var live = await client.GetAsync("/health/live");
        using var ready = await client.GetAsync("/health/ready");
        using var info = await client.GetAsync("/api/system/info");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal(HttpStatusCode.OK, info.StatusCode);
    }

    [Fact]
    public async Task AuditEndpoint_AsAdministrator_ReturnsNewestFirst()
    {
        using var client = new ApiTestClient(Factory);
        await client.RegisterAndAuthenticateAsync("audit-reader@example.test", "Audit Reader");

        using var response = await client.GetAsync("/api/admin/audit?page=1&pageSize=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await ApiTestClient.ReadJsonAsync(response);
        var items = page.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 1);

        var timestamps = items.EnumerateArray()
            .Select(i => i.GetProperty("occurredAtUtc").GetDateTimeOffset())
            .ToArray();

        for (var i = 1; i < timestamps.Length; i++)
        {
            Assert.True(timestamps[i - 1] >= timestamps[i], "Audit records must be returned newest first.");
        }
    }
}
