using Microsoft.AspNetCore.Antiforgery;
using TraceForge.Api.Configuration;
using TraceForge.Application.Accounts;

namespace TraceForge.Api.Endpoints;

public static class AuthEndpoints
{
    public sealed record RegisterRequest(string? Email, string? Password, string? DisplayName);

    public sealed record SignInRequest(string? Email, string? Password);

    public sealed record CsrfTokenResponse(string Token);

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        var auth = api.MapGroup("/auth").WithTags("Authentication");

        auth.MapGet("/csrf", static (HttpContext httpContext, IAntiforgery antiforgery) =>
            {
                var tokens = antiforgery.GetAndStoreTokens(httpContext);
                return TypedResults.Ok(new CsrfTokenResponse(tokens.RequestToken!));
            })
            .AllowAnonymous()
            .WithName("GetCsrfToken")
            .WithSummary("Issues an antiforgery token and its cookie. Required before unsafe requests.");

        auth.MapPost("/register", static async (
                RegisterRequest request,
                IAccountService accounts,
                CancellationToken cancellationToken) =>
            {
                var result = await accounts.RegisterAsync(
                    new RegisterAccountCommand(
                        request.Email ?? string.Empty,
                        request.Password ?? string.Empty,
                        request.DisplayName ?? string.Empty),
                    cancellationToken);

                return result.Succeeded
                    ? Results.Created("/api/auth/me", result.User)
                    : ToProblem(result);
            })
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("RegisterAccount")
            .WithSummary("Creates an account. The first account of a fresh installation becomes Administrator.");

        auth.MapPost("/login", static async (
                SignInRequest request,
                IAccountService accounts,
                CancellationToken cancellationToken) =>
            {
                var result = await accounts.SignInAsync(
                    new SignInCommand(request.Email ?? string.Empty, request.Password ?? string.Empty),
                    cancellationToken);

                return result.Succeeded ? Results.Ok(result.User) : ToProblem(result);
            })
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("SignIn")
            .WithSummary("Verifies credentials and starts a session cookie.");

        auth.MapPost("/logout", static async (
                IAccountService accounts,
                CancellationToken cancellationToken) =>
            {
                await accounts.SignOutAsync(cancellationToken);
                return Results.NoContent();
            })
            .RequireAuthorization()
            .WithName("SignOut")
            .WithSummary("Ends the current session.");

        auth.MapGet("/me", static async (IAccountService accounts, CancellationToken cancellationToken) =>
            {
                var account = await accounts.GetCurrentAsync(cancellationToken);
                return account is null ? Results.Unauthorized() : Results.Ok(account);
            })
            .RequireAuthorization()
            .WithName("GetCurrentAccount")
            .WithSummary("Returns the authenticated account.");

        return api;
    }

    internal static IResult ToProblem(AccountResult result)
    {
        var errors = result.Errors.Count == 0 ? null : result.Errors;
        var extensions = errors is null
            ? null
            : new Dictionary<string, object?> { ["errors"] = errors };

        return result.Failure switch
        {
            AccountFailureReason.InvalidInput => Problem(
                StatusCodes.Status400BadRequest,
                "Invalid input",
                "One or more fields are invalid.",
                extensions),
            AccountFailureReason.DuplicateAccount => Problem(
                StatusCodes.Status409Conflict,
                "Account already exists",
                "An account with this e-mail address already exists.",
                extensions),
            AccountFailureReason.WeakPassword => Problem(
                StatusCodes.Status400BadRequest,
                "Password rejected",
                "The password does not satisfy the password policy.",
                extensions),
            AccountFailureReason.RegistrationDisabled => Problem(
                StatusCodes.Status403Forbidden,
                "Registration disabled",
                "Self-registration is disabled on this instance.",
                extensions),
            AccountFailureReason.InvalidCredentials => Problem(
                StatusCodes.Status401Unauthorized,
                "Invalid credentials",
                "Invalid e-mail or password.",
                null),
            AccountFailureReason.LockedOut => Problem(
                StatusCodes.Status423Locked,
                "Account locked",
                "The account is temporarily locked after repeated failed sign-in attempts.",
                null),
            _ => Problem(
                StatusCodes.Status400BadRequest,
                "Request failed",
                "The request could not be completed.",
                extensions),
        };
    }

    private static IResult Problem(
        int statusCode,
        string title,
        string detail,
        IDictionary<string, object?>? extensions)
    {
        return Results.Problem(
            statusCode: statusCode,
            title: title,
            detail: detail,
            extensions: extensions);
    }
}
