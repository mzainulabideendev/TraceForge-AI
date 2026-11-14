using Microsoft.AspNetCore.Antiforgery;

namespace TraceForge.Api.Security;

/// <summary>
/// Enforces the antiforgery double-submit contract for every unsafe request on the /api surface:
/// the X-XSRF-TOKEN header must match the token bound to the XSRF-TOKEN cookie and the current
/// identity. Safe methods (GET/HEAD/OPTIONS) pass through.
/// </summary>
internal sealed class AntiforgeryEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var method = httpContext.Request.Method;

        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
        {
            return await next(context);
        }

        var antiforgery = httpContext.RequestServices.GetRequiredService<IAntiforgery>();

        try
        {
            await antiforgery.ValidateRequestAsync(httpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Antiforgery validation failed",
                detail: "The request is missing a valid antiforgery token. Fetch /api/auth/csrf and send its token in the X-XSRF-TOKEN header.");
        }

        return await next(context);
    }
}
