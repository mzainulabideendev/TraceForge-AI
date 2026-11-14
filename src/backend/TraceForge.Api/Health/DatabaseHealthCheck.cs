using Microsoft.Extensions.Diagnostics.HealthChecks;
using TraceForge.Infrastructure.Persistence;

namespace TraceForge.Api.Health;

/// <summary>
/// Readiness check that actually opens the configured database. A process that cannot reach its
/// database must never be advertised as ready.
/// </summary>
internal sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly IServiceScopeFactory _scopeFactory;

    public DatabaseHealthCheck(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TraceForgeDbContext>();

            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
            var provider = dbContext.Database.ProviderName ?? "unknown";

            return canConnect
                ? HealthCheckResult.Healthy(
                    "Database connection succeeded.",
                    new Dictionary<string, object> { ["provider"] = provider })
                : HealthCheckResult.Unhealthy(
                    "Database connection failed.",
                    data: new Dictionary<string, object> { ["provider"] = provider });
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Database check failed.", exception);
        }
    }
}
