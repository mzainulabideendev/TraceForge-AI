using System.Globalization;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TraceForge.Api.Health;

/// <summary>
/// Verifies that the log directory used by the file sink is actually writable.
/// This is a real dependency of the running process, so readiness reports it.
/// </summary>
internal sealed class LogDirectoryHealthCheck : IHealthCheck
{
    private readonly IHostEnvironment _environment;

    public LogDirectoryHealthCheck(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = Path.Combine(_environment.ContentRootPath, "logs");
            Directory.CreateDirectory(directory);

            var probePath = Path.Combine(directory, $"health-probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probePath, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            File.Delete(probePath);

            return Task.FromResult(HealthCheckResult.Healthy(
                "Log directory is writable.",
                new Dictionary<string, object> { ["path"] = directory }));
        }
        catch (Exception exception)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Log directory is not writable.",
                exception,
                new Dictionary<string, object> { ["contentRoot"] = _environment.ContentRootPath }));
        }
    }
}
