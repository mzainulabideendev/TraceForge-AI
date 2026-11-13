using Serilog.Context;

namespace TraceForge.Api.Middleware;

/// <summary>
/// Accepts a caller-supplied correlation id when it is well formed, otherwise generates one.
/// The resolved id is echoed back, becomes the ASP.NET Core trace identifier (used by problem
/// details) and is pushed into the structured log context for the duration of the request.
/// </summary>
internal sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Request-Id";

    private const int MaxLength = 128;

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);

        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var supplied))
        {
            var candidate = supplied.ToString();

            if (IsWellFormed(candidate))
            {
                return candidate;
            }
        }

        return Guid.NewGuid().ToString("N");
    }

    private static bool IsWellFormed(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.'))
            {
                return false;
            }
        }

        return true;
    }
}
