using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TraceForge.Application.Accounts;
using TraceForge.Application.Audit;
using TraceForge.Application.Common;
using TraceForge.Infrastructure.Audit;
using TraceForge.Infrastructure.Configuration;
using TraceForge.Infrastructure.Http;
using TraceForge.Infrastructure.Identity;
using TraceForge.Infrastructure.Persistence;

namespace TraceForge.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers persistence, identity, audit storage and the account use cases.
    /// Transport concerns (cookie policy, authorization policies) are configured by the API layer.
    /// </summary>
    public static IServiceCollection AddTraceForgeInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .Validate(
                options => options.SessionMinutes is >= AuthOptions.MinSessionMinutes and <= AuthOptions.MaxSessionMinutes,
                $"TraceForge:Auth:SessionMinutes must be between {AuthOptions.MinSessionMinutes} and {AuthOptions.MaxSessionMinutes}.")
            .Validate(
                options => options.MaxFailedAttempts is >= AuthOptions.MinFailedAttempts and <= AuthOptions.MaxFailedAttemptsAllowed,
                $"TraceForge:Auth:MaxFailedAttempts must be between {AuthOptions.MinFailedAttempts} and {AuthOptions.MaxFailedAttemptsAllowed}.")
            .Validate(
                options => options.LockoutMinutes is >= AuthOptions.MinLockoutMinutes and <= AuthOptions.MaxLockoutMinutes,
                $"TraceForge:Auth:LockoutMinutes must be between {AuthOptions.MinLockoutMinutes} and {AuthOptions.MaxLockoutMinutes}.")
            .ValidateOnStart();

        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .Validate(
                options => options.IsSqlite || options.IsPostgreSql,
                $"TraceForge:Database:Provider must be '{DatabaseOptions.SqliteProvider}' or '{DatabaseOptions.PostgreSqlProvider}'.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ConnectionString),
                "TraceForge:Database:ConnectionString is required.")
            .ValidateOnStart();

        services.AddHttpContextAccessor();
        services.AddScoped<IRequestContext, HttpRequestContext>();

        services.AddDbContext<TraceForgeDbContext>((provider, options) =>
        {
            var database = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

            if (database.IsPostgreSql)
            {
                options.UseNpgsql(
                    database.ConnectionString,
                    npgsql => npgsql.MigrationsAssembly(DatabaseOptions.PostgreSqlMigrationsAssembly));
            }
            else
            {
                options.UseSqlite(
                    database.ConnectionString,
                    sqlite => sqlite.MigrationsAssembly(DatabaseOptions.SqliteMigrationsAssembly));
            }
        });

        AddIdentity(services);

        services.AddScoped<IAuditLogWriter, AuditLogWriter>();
        services.AddScoped<IAuditLogReader, AuditLogReader>();
        services.AddScoped<IAccountService, AccountService>();

        return services;
    }

    private static void AddIdentity(IServiceCollection services)
    {
        services
            .AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<TraceForgeDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services
            .AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies();

        services.AddOptions<IdentityOptions>()
            .Configure<IOptions<AuthOptions>>((identity, auth) =>
            {
                identity.User.RequireUniqueEmail = true;

                // Length over composition, per current NIST guidance; 12 characters minimum.
                identity.Password.RequiredLength = 12;
                identity.Password.RequiredUniqueChars = 4;
                identity.Password.RequireDigit = true;
                identity.Password.RequireLowercase = true;
                identity.Password.RequireUppercase = true;
                identity.Password.RequireNonAlphanumeric = false;

                identity.Lockout.AllowedForNewUsers = true;
                identity.Lockout.MaxFailedAccessAttempts = auth.Value.MaxFailedAttempts;
                identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(auth.Value.LockoutMinutes);

                identity.SignIn.RequireConfirmedEmail = false;
                identity.SignIn.RequireConfirmedAccount = false;
            });
    }
}
