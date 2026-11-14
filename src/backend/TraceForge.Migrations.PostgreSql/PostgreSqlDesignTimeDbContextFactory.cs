using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TraceForge.Infrastructure.Persistence;

namespace TraceForge.Migrations.PostgreSql;

/// <summary>
/// Design-time context used by `dotnet ef` to produce PostgreSQL migrations without a live server.
/// Applying them requires a real PostgreSQL instance (see docs/development/setup.md).
/// </summary>
public sealed class PostgreSqlDesignTimeDbContextFactory : IDesignTimeDbContextFactory<TraceForgeDbContext>
{
    public TraceForgeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TraceForgeDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=traceforge_design;Username=traceforge;Password=design-time-only",
                npgsql => npgsql.MigrationsAssembly(typeof(PostgreSqlDesignTimeDbContextFactory).Assembly.GetName().Name))
            .Options;

        return new TraceForgeDbContext(options);
    }
}
