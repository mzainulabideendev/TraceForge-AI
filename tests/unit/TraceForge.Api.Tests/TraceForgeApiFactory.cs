using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TraceForge.Infrastructure.Persistence;

namespace TraceForge.Api.Tests;

/// <summary>
/// Boots the real API against a real, migrated SQLite database file unique to this factory.
/// Nothing is mocked: authentication, antiforgery, authorization and persistence all run.
/// xUnit permits a single public constructor on a class fixture, so overrides go through
/// <see cref="WithConfiguration"/>.
/// </summary>
public sealed class TraceForgeApiFactory : WebApplicationFactory<Program>
{
    private readonly IReadOnlyDictionary<string, string?> _configurationOverrides;
    private readonly string _databasePath;

    public TraceForgeApiFactory()
        : this(new Dictionary<string, string?>())
    {
    }

    internal TraceForgeApiFactory(IReadOnlyDictionary<string, string?> configurationOverrides)
    {
        _configurationOverrides = configurationOverrides;
        _databasePath = Path.Combine(
            Path.GetTempPath(),
            $"traceforge-tests-{Guid.NewGuid():N}.db");
    }

    /// <summary>Creates a factory whose application configuration is overridden by the given values.</summary>
    public static TraceForgeApiFactory WithConfiguration(params (string Key, string? Value)[] settings)
    {
        var overrides = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in settings)
        {
            overrides[key] = value;
        }

        return new TraceForgeApiFactory(overrides);
    }

    /// <summary>Runs a scoped action against the same database the API uses.</summary>
    public async Task<T> WithDatabaseAsync<T>(Func<TraceForgeDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TraceForgeDbContext>();
        return await action(dbContext);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["TraceForge:Auth:AllowSelfRegistration"] = "true",
                ["TraceForge:Auth:RequireSecureCookies"] = "false",
                // Test isolation: suites issue many requests from a single "unknown" client IP, so the
                // production-sized budgets would otherwise cause cross-test 429s. The limiter itself
                // is exercised by dedicated tests that override these values.
                ["TraceForge:RateLimiting:PermitLimit"] = "10000",
                ["TraceForge:RateLimiting:AuthPermitLimit"] = "1000",
                ["TraceForge:Database:Provider"] = "Sqlite",
                ["TraceForge:Database:ConnectionString"] = $"Data Source={_databasePath}",
                ["TraceForge:Database:ApplyMigrationsOnStartup"] = "true",
            };

            foreach (var (key, value) in _configurationOverrides)
            {
                settings[key] = value;
            }

            configurationBuilder.AddInMemoryCollection(settings);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            try
            {
                File.Delete(_databasePath + suffix);
            }
            catch (IOException)
            {
                // A locked file is not worth failing a test run over; temp files are cleaned by the OS.
            }
        }
    }
}
