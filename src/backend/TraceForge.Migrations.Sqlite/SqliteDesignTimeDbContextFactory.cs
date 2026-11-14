using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TraceForge.Infrastructure.Persistence;

namespace TraceForge.Migrations.Sqlite;

/// <summary>
/// Design-time context used by `dotnet ef` to produce SQLite migrations without booting the API.
/// The connection string is only used for tooling; it is never used at runtime.
/// </summary>
public sealed class SqliteDesignTimeDbContextFactory : IDesignTimeDbContextFactory<TraceForgeDbContext>
{
    public TraceForgeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TraceForgeDbContext>()
            .UseSqlite(
                "Data Source=traceforge-design-time.db",
                sqlite => sqlite.MigrationsAssembly(typeof(SqliteDesignTimeDbContextFactory).Assembly.GetName().Name))
            .Options;

        return new TraceForgeDbContext(options);
    }
}
