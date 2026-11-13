using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using TraceForge.Api.Configuration;
using TraceForge.Api.Health;

namespace TraceForge.Api.Endpoints;

public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapTraceForgeSystem(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = static _ => false,
            ResponseWriter = HealthCheckResponseWriter.WriteAsync,
        })
        .WithTags("Health")
        .WithSummary("Liveness probe. Succeeds whenever the process is serving requests.");

        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = static check => check.Tags.Contains("ready"),
            ResponseWriter = HealthCheckResponseWriter.WriteAsync,
        })
        .WithTags("Health")
        .WithSummary("Readiness probe. Succeeds only when required runtime dependencies are usable.");

        var api = endpoints.MapGroup("/api")
            .RequireRateLimiting(RateLimitPolicies.Api)
            .WithTags("System");

        api.MapGet("/system/info", static (
                IOptions<TraceForgeOptions> options,
                ApplicationLifetimeInfo lifetime,
                IHostEnvironment environment,
                TimeProvider timeProvider) =>
            {
                var snapshot = new SystemInfoResponse(
                    Application: TraceForgeIdentity.Name,
                    InstanceName: options.Value.InstanceName,
                    Version: TraceForgeIdentity.InformationalVersion,
                    Environment: environment.EnvironmentName,
                    ServerTimeUtc: timeProvider.GetUtcNow(),
                    StartedAtUtc: lifetime.StartedAt,
                    UptimeSeconds: Math.Round(lifetime.Uptime.TotalSeconds, 1));

                return TypedResults.Ok(snapshot);
            })
            .WithName("GetSystemInfo")
            .WithSummary("Reports real runtime identity, version and uptime for this instance.");

        return endpoints;
    }

    public sealed record SystemInfoResponse(
        string Application,
        string InstanceName,
        string Version,
        string Environment,
        DateTimeOffset ServerTimeUtc,
        DateTimeOffset StartedAtUtc,
        double UptimeSeconds);
}
