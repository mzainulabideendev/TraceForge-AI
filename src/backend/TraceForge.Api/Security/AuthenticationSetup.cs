using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using TraceForge.Domain.Identity;
using TraceForge.Infrastructure.Configuration;

namespace TraceForge.Api.Security;

public static class AuthorizationPolicies
{
    /// <summary>Requires the Administrator role.</summary>
    public const string AdministratorOnly = "AdministratorOnly";
}

public static class AuthenticationSetup
{
    public const string AntiforgeryHeaderName = "X-XSRF-TOKEN";
    public const string AntiforgeryCookieName = "XSRF-TOKEN";
    public const string AuthCookieName = "traceforge.auth";

    /// <summary>
    /// Configures cookie authentication, the antiforgery double-submit contract used by the SPA,
    /// and role-based authorization policies.
    /// </summary>
    public static IServiceCollection AddTraceForgeAuthentication(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.AdministratorOnly, policy =>
                policy.RequireAuthenticatedUser().RequireRole(ApplicationRoles.Administrator));

        services.AddAntiforgery(options =>
        {
            options.HeaderName = AntiforgeryHeaderName;
            options.Cookie.Name = AntiforgeryCookieName;
            options.Cookie.HttpOnly = false; // Read by the SPA to send the header value.
            options.Cookie.IsEssential = true;
            options.Cookie.Path = "/";
            options.Cookie.SameSite = SameSiteMode.Strict;
        });

        // Cookie hardening is bound through the options system, exactly like IdentityOptions, so
        // late configuration sources (deployment secret stores, test overrides) are not lost.
        services.AddOptions<AntiforgeryOptions>()
            .Configure<IOptions<AuthOptions>>((options, auth) =>
                options.Cookie.SecurePolicy = SecurePolicy(auth.Value));

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = AuthCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.Path = "/";
            options.SlidingExpiration = true;

            // JSON APIs must not answer with 302 redirects to a login page.
            options.Events.OnRedirectToLogin = context => WriteProblemAsync(
                context.HttpContext,
                StatusCodes.Status401Unauthorized,
                "Authentication required",
                "This operation requires an authenticated session.");

            options.Events.OnRedirectToAccessDenied = context => WriteProblemAsync(
                context.HttpContext,
                StatusCodes.Status403Forbidden,
                "Forbidden",
                "Your account does not have permission to perform this operation.");
        });

        // PostConfigure runs after ConfigureApplicationCookie's own defaults, so these values win.
        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .PostConfigure<IOptions<AuthOptions>>((options, auth) =>
            {
                options.Cookie.SecurePolicy = SecurePolicy(auth.Value);
                options.ExpireTimeSpan = TimeSpan.FromMinutes(auth.Value.SessionMinutes);
            });

        return services;
    }

    private static CookieSecurePolicy SecurePolicy(AuthOptions auth) =>
        auth.RequireSecureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;

    private static Task WriteProblemAsync(HttpContext context, int statusCode, string title, string detail)
    {
        context.Response.StatusCode = statusCode;
        return Results.Problem(statusCode: statusCode, title: title, detail: detail).ExecuteAsync(context);
    }
}
