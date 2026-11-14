namespace TraceForge.Infrastructure.Configuration;

/// <summary>
/// Authentication and account policy. Bound from "TraceForge:Auth" and validated at startup.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "TraceForge:Auth";

    /// <summary>
    /// Allows anyone who can reach the API to create an Analyst account. The first account of a
    /// fresh installation always becomes Administrator so the product is bootstrappable.
    /// </summary>
    public bool AllowSelfRegistration { get; init; }

    /// <summary>Marks the auth cookie Secure. Must stay true outside local HTTP development.</summary>
    public bool RequireSecureCookies { get; init; } = true;

    public int SessionMinutes { get; init; } = 480;

    public int MaxFailedAttempts { get; init; } = 5;

    public int LockoutMinutes { get; init; } = 15;

    public const int MinSessionMinutes = 5;
    public const int MaxSessionMinutes = 10_080;
    public const int MinFailedAttempts = 3;
    public const int MaxFailedAttemptsAllowed = 20;
    public const int MinLockoutMinutes = 1;
    public const int MaxLockoutMinutes = 1_440;
}

/// <summary>Database provider selection. Bound from "TraceForge:Database".</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "TraceForge:Database";

    public const string SqliteProvider = "Sqlite";
    public const string PostgreSqlProvider = "PostgreSql";
    public const string SqliteMigrationsAssembly = "TraceForge.Migrations.Sqlite";
    public const string PostgreSqlMigrationsAssembly = "TraceForge.Migrations.PostgreSql";

    public string Provider { get; init; } = SqliteProvider;

    /// <summary>
    /// Development default uses a local SQLite file; deployed environments must supply a
    /// PostgreSQL connection string through environment variables or secret storage.
    /// </summary>
    public string ConnectionString { get; init; } = "Data Source=traceforge.db";

    /// <summary>Applies pending migrations at startup. Development-only default; explicit in production.</summary>
    public bool ApplyMigrationsOnStartup { get; init; }

    public bool IsPostgreSql =>
        string.Equals(Provider, PostgreSqlProvider, StringComparison.OrdinalIgnoreCase);

    public bool IsSqlite =>
        string.Equals(Provider, SqliteProvider, StringComparison.OrdinalIgnoreCase);
}
