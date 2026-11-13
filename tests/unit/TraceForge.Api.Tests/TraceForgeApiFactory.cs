using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace TraceForge.Api.Tests;

/// <summary>
/// Boots the real API with the real pipeline. Tests exercise actual HTTP behaviour; nothing is mocked.
/// xUnit permits a single public constructor on a class fixture, so configuration overrides are
/// supplied through <see cref="WithConfiguration"/>.
/// </summary>
public sealed class TraceForgeApiFactory : WebApplicationFactory<Program>
{
    private readonly IReadOnlyDictionary<string, string?> _configurationOverrides;

    public TraceForgeApiFactory()
        : this(new Dictionary<string, string?>())
    {
    }

    internal TraceForgeApiFactory(IReadOnlyDictionary<string, string?> configurationOverrides)
    {
        _configurationOverrides = configurationOverrides;
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            configurationBuilder.AddInMemoryCollection(_configurationOverrides);
        });
    }
}
