using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TraceForge.Domain.Identity;

namespace TraceForge.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    private static readonly Action<ILogger, string?, int, string, Exception?> DatabaseReady =
        LoggerMessage.Define<string?, int, string>(
            LogLevel.Information,
            new EventId(1, "DatabaseReady"),
            "Database ready. Provider={Provider} PendingMigrationsApplied={PendingCount} RolesSeeded={Roles}");

    /// <summary>
    /// Applies pending migrations and seeds the role catalogue. Safe to run repeatedly.
    /// Returns a summary so the caller can log exactly what happened.
    /// </summary>
    public static async Task<DatabaseInitializationResult> InitializeAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        var dbContext = provider.GetRequiredService<TraceForgeDbContext>();
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DatabaseInitializer));

        var pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
        await dbContext.Database.MigrateAsync(cancellationToken);

        var seeded = new List<string>();
        foreach (var role in ApplicationRoles.All)
        {
            if (await roleManager.RoleExistsAsync(role))
            {
                continue;
            }

            var result = await roleManager.CreateAsync(new IdentityRole<Guid>(role));
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to seed role '{role}': {string.Join("; ", result.Errors.Select(e => e.Description))}");
            }

            seeded.Add(role);
        }

        var providerName = dbContext.Database.ProviderName;
        var seededDescription = seeded.Count == 0 ? "none" : string.Join(",", seeded);

        DatabaseReady(logger, providerName, pending.Length, seededDescription, null);

        return new DatabaseInitializationResult(pending.Length, seeded);
    }
}

public sealed record DatabaseInitializationResult(int MigrationsApplied, IReadOnlyList<string> RolesSeeded);
