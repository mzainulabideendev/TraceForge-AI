using System.Net;
using Microsoft.EntityFrameworkCore;
using TraceForge.Domain.Audit;

namespace TraceForge.Api.Tests;

public sealed class AuthFlowTests : ApiTestBase
{
    [Fact]
    public async Task Register_FirstAccount_BecomesAdministrator_AndIsSignedIn()
    {
        using var client = new ApiTestClient(Factory);

        using var response = await client.RegisterAsync("first-admin@example.test", "First Admin");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = await ApiTestClient.ReadJsonAsync(response);
        Assert.Equal("first-admin@example.test", user.GetProperty("email").GetString());
        Assert.Equal("Administrator", user.GetProperty("roles")[0].GetString());

        using var meResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
    }

    [Fact]
    public async Task Register_WithoutAntiforgeryToken_IsRejected()
    {
        using var client = new ApiTestClient(Factory);

        using var response = await client.PostJsonAsync(
            "/api/auth/register",
            new { email = "no-token@example.test", password = ApiTestClient.StrongPassword, displayName = "No Token" },
            includeCsrfToken: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await ApiTestClient.ReadJsonAsync(response);
        Assert.Contains("Antiforgery", problem.GetProperty("title").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsConflict()
    {
        using var client = new ApiTestClient(Factory);
        using var first = await client.RegisterAsync("duplicate@example.test", "Original Owner");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Registering signed the client in, which rebinds the antiforgery token to the new identity.
        await client.FetchCsrfTokenAsync();
        using var second = await client.RegisterAsync("duplicate@example.test", "Copy Cat");

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Register_WeakPassword_ReturnsBadRequestWithPolicyErrors()
    {
        using var client = new ApiTestClient(Factory);

        using var response = await client.RegisterAsync("weak@example.test", "Weak Password", password: "short");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ApiTestClient.ReadJsonAsync(response);
        Assert.Equal("Password rejected", problem.GetProperty("title").GetString());
        Assert.True(problem.GetProperty("errors").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Register_InvalidEmail_ReturnsBadRequest()
    {
        using var client = new ApiTestClient(Factory);

        using var response = await client.RegisterAsync("not-an-email", "Bad Email");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ApiTestClient.ReadJsonAsync(response);
        Assert.Equal("Invalid input", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Register_WhenSelfRegistrationDisabled_StillBootstrapsFirstAccount_ThenRejectsOthers()
    {
        using var factory = TraceForgeApiFactory.WithConfiguration(
            ("TraceForge:Auth:AllowSelfRegistration", "false"));

        using var bootstrap = new ApiTestClient(factory);
        using var first = await bootstrap.RegisterAsync("bootstrap-only@example.test", "Bootstrap Admin");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var second = new ApiTestClient(factory);
        await second.FetchCsrfTokenAsync();
        using var response = await second.RegisterAsync("closed-registration@example.test", "Rejected Analyst");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await ApiTestClient.ReadJsonAsync(response);
        Assert.Equal("Registration disabled", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized_AndIsAudited()
    {
        using var client = new ApiTestClient(Factory);
        await client.RegisterAndAuthenticateAsync("wrong-password@example.test", "Wrong Password");

        using var response = await client.LoginAsync("wrong-password@example.test", "DefinitelyWrong1!");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var failedAudits = await Factory.WithDatabaseAsync(db => db.AuditLogs
            .Where(a => a.Action == AuditActions.UserLoginFailed && a.ActorIdentifier == "wrong-password@example.test")
            .CountAsync());
        Assert.True(failedAudits >= 1);
    }

    [Fact]
    public async Task Login_LocksAccount_AfterRepeatedFailures()
    {
        using var client = new ApiTestClient(Factory);
        await client.RegisterAndAuthenticateAsync("lockout@example.test", "Lockout Subject");

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            // Re-fetch the token before each attempt: a failed login does not change identity,
            // but this mirrors what a real browser client does.
            await client.FetchCsrfTokenAsync();
            using var response = await client.LoginAsync("lockout@example.test", "WrongPassword1!");
            statuses.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.Locked, statuses);

        await client.FetchCsrfTokenAsync();
        using var correctPasswordAttempt = await client.LoginAsync("lockout@example.test", ApiTestClient.StrongPassword);
        Assert.Equal(HttpStatusCode.Locked, correctPasswordAttempt.StatusCode);
        Assert.Equal("application/problem+json", correctPasswordAttempt.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Logout_EndsSession_AndSubsequentRequestsAreUnauthorized()
    {
        using var client = new ApiTestClient(Factory);
        await client.RegisterAndAuthenticateAsync("logout@example.test", "Logout Subject");

        using var logout = await client.PostAsync("/api/auth/logout");
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);

        using var admin = await client.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.Unauthorized, admin.StatusCode);
    }

    [Fact]
    public async Task RegistrationAndLogin_WriteAuditRecords()
    {
        using var client = new ApiTestClient(Factory);
        await client.RegisterAndAuthenticateAsync("audited@example.test", "Audited User");

        await client.FetchCsrfTokenAsync();
        using var logout = await client.PostAsync("/api/auth/logout");
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        await client.FetchCsrfTokenAsync();
        using var login = await client.LoginAsync("audited@example.test", ApiTestClient.StrongPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var actions = await Factory.WithDatabaseAsync(db => db.AuditLogs
            .Where(a => a.ActorIdentifier == "audited@example.test")
            .Select(a => a.Action)
            .ToListAsync());

        Assert.Contains(AuditActions.UserRegistered, actions);
        Assert.Contains(AuditActions.UserLogout, actions);
        Assert.Contains(AuditActions.UserLoginSucceeded, actions);
    }

    [Fact]
    public async Task AuthenticationCookie_IsHttpOnly_SameSiteStrict_AndPathScoped()
    {
        using var client = new ApiTestClient(Factory);
        await client.FetchCsrfTokenAsync();

        using var response = await client.RegisterAsync("cookie@example.test", "Cookie Subject");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var authCookie = response.Headers
            .GetValues("Set-Cookie")
            .FirstOrDefault(value => value.StartsWith("traceforge.auth=", StringComparison.Ordinal));

        Assert.NotNull(authCookie);
        Assert.Contains("httponly", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", authCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", authCookie, StringComparison.OrdinalIgnoreCase);

        // The test factory deliberately runs over plain HTTP with RequireSecureCookies=false.
        Assert.DoesNotContain("secure", authCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SessionCookie_IsMarkedSecure_WhenConfigured()
    {
        using var secureFactory = TraceForgeApiFactory.WithConfiguration(
            ("TraceForge:Auth:RequireSecureCookies", "true"));

        // ASP.NET Core refuses to emit Secure cookies over a non-TLS request, so this flow runs
        // over https. That is the only configuration in which RequireSecureCookies=true is valid.
        using var client = new ApiTestClient(secureFactory, new Uri("https://localhost"));
        await client.FetchCsrfTokenAsync();

        using var response = await client.RegisterAsync("secure-cookie@example.test", "Secure Cookie");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var authCookie = response.Headers
            .GetValues("Set-Cookie")
            .FirstOrDefault(value => value.StartsWith("traceforge.auth=", StringComparison.Ordinal));

        Assert.NotNull(authCookie);
        Assert.Contains("secure", authCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginEndpoint_IsRateLimitedPerClient()
    {
        using var factory = TraceForgeApiFactory.WithConfiguration(
            ("TraceForge:RateLimiting:AuthPermitLimit", "2"),
            ("TraceForge:RateLimiting:AuthWindowSeconds", "60"));
        using var client = new ApiTestClient(factory);
        await client.FetchCsrfTokenAsync();

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var response = await client.LoginAsync("nobody@example.test", "WrongPassword1!");
            statuses.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }
}
