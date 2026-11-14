namespace TraceForge.Api.Middleware;

/// <summary>
/// Applies defensive response headers to every response.
///
/// <para>API responses keep a maximally restrictive policy: nothing should ever be rendered from a
/// JSON response. When the deployment serves the Angular console from the same origin, the document
/// responses need a policy that allows the application's own bundle and its runtime-inlined
/// component styles. Scripts stay restricted to same-origin files; no CDN, inline script or eval is
/// permitted.</para>
/// </summary>
internal sealed class SecurityHeadersMiddleware
{
    private const string ApiContentSecurityPolicy =
        "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    private const string SpaContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "frame-ancestors 'none'; " +
        "form-action 'self'";

    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        var path = context.Request.Path;

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "accelerometer=(), camera=(), geolocation=(), microphone=(), payment=()";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";

        // A request without a file extension that is not an API or health route is a single-page
        // application document request; everything else is an API or asset response.
        var isSpaDocument =
            !path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase) &&
            !System.IO.Path.HasExtension(path.Value);

        headers["Content-Security-Policy"] =
            isSpaDocument ? SpaContentSecurityPolicy : ApiContentSecurityPolicy;

        if (isSpaDocument)
        {
            headers["Cache-Control"] = "no-cache";
        }

        if (path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            headers["Cache-Control"] = "no-store";
        }

        if (context.Request.IsHttps)
        {
            headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
        }

        return _next(context);
    }
}
