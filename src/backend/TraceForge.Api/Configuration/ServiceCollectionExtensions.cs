using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using TraceForge.Api.Health;

namespace TraceForge.Api.Configuration;

public static class RateLimitPolicies
{
    /// <summary>Fixed-window limiter applied to the /api surface.</summary>
    public const string Api = "api";

    /// <summary>Stricter limiter applied to credential endpoints (login, registration).</summary>
    public const string Auth = "auth";
}

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything the TraceForge API needs to run: validated options, problem details,
    /// OpenAPI, request rate limiting and health checks.
    /// </summary>
    public static IServiceCollection AddTraceForgeCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<TraceForgeOptions>()
            .Bind(configuration.GetSection(TraceForgeOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.InstanceName),
                "TraceForge:InstanceName must be a non-empty value.")
            .Validate(
                options => options.InstanceName.Trim().Length is >= 1 and <= 120,
                "TraceForge:InstanceName must be between 1 and 120 characters.")
            .Validate(
                options => options.RateLimiting is not null,
                "TraceForge:RateLimiting must be a configuration object.")
            .Validate(
                options => options.RateLimiting.PermitLimit is >= RateLimitingOptions.MinPermitLimit and <= RateLimitingOptions.MaxPermitLimit,
                $"TraceForge:RateLimiting:PermitLimit must be between {RateLimitingOptions.MinPermitLimit} and {RateLimitingOptions.MaxPermitLimit}.")
            .Validate(
                options => options.RateLimiting.WindowSeconds is >= RateLimitingOptions.MinWindowSeconds and <= RateLimitingOptions.MaxWindowSeconds,
                $"TraceForge:RateLimiting:WindowSeconds must be between {RateLimitingOptions.MinWindowSeconds} and {RateLimitingOptions.MaxWindowSeconds}.")
            .Validate(
                options => options.RateLimiting.QueueLimit >= RateLimitingOptions.MinQueueLimit,
                $"TraceForge:RateLimiting:QueueLimit must be {RateLimitingOptions.MinQueueLimit} or greater.")
            .Validate(
                options => options.RateLimiting.AuthPermitLimit is >= RateLimitingOptions.MinPermitLimit and <= RateLimitingOptions.MaxPermitLimit,
                $"TraceForge:RateLimiting:AuthPermitLimit must be between {RateLimitingOptions.MinPermitLimit} and {RateLimitingOptions.MaxPermitLimit}.")
            .Validate(
                options => options.RateLimiting.AuthWindowSeconds is >= RateLimitingOptions.MinWindowSeconds and <= RateLimitingOptions.MaxWindowSeconds,
                $"TraceForge:RateLimiting:AuthWindowSeconds must be between {RateLimitingOptions.MinWindowSeconds} and {RateLimitingOptions.MaxWindowSeconds}.")
            .ValidateOnStart();

        services.AddSingleton<ApplicationLifetimeInfo>();
        services.AddSingleton(TimeProvider.System);

        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Instance ??= context.HttpContext.Request.Path.Value;
                context.ProblemDetails.Extensions["application"] = TraceForgeIdentity.Name;

                // Make every problem response traceable to the correlation id echoed in X-Request-Id.
                // The framework otherwise reports an Activity id that operators cannot correlate.
                context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
            };
        });

        services.AddOpenApi(TraceForgeIdentity.ApiDocumentName);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(RateLimitPolicies.Api, static httpContext =>
            {
                var limits = httpContext.RequestServices
                    .GetRequiredService<IOptions<TraceForgeOptions>>()
                    .Value
                    .RateLimiting;

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ResolvePartitionKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.PermitLimit,
                        Window = TimeSpan.FromSeconds(limits.WindowSeconds),
                        QueueLimit = limits.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true,
                    });
            });

            options.AddPolicy(RateLimitPolicies.Auth, static httpContext =>
            {
                var limits = httpContext.RequestServices
                    .GetRequiredService<IOptions<TraceForgeOptions>>()
                    .Value
                    .RateLimiting;

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: $"auth:{ResolvePartitionKey(httpContext)}",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.AuthPermitLimit,
                        Window = TimeSpan.FromSeconds(limits.AuthWindowSeconds),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true,
                    });
            });

            options.OnRejected = static async (context, cancellationToken) =>
            {
                var problemDetailsService =
                    context.HttpContext.RequestServices.GetService<IProblemDetailsService>();

                if (problemDetailsService is null)
                {
                    context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    return;
                }

                await problemDetailsService.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails = new Microsoft.AspNetCore.Mvc.ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many requests",
                        Detail = "The request rate limit for this client has been exceeded. Retry after the current window.",
                    },
                });
            };
        });

        services.AddHealthChecks()
            .AddCheck<LogDirectoryHealthCheck>("log-directory", tags: ["ready"])
            .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

        return services;
    }

    private static string ResolvePartitionKey(HttpContext httpContext)
    {
        var address = httpContext.Connection.RemoteIpAddress;
        return address is null ? "unknown" : address.ToString();
    }
}
