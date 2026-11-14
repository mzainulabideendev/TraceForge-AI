using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TraceForge.Domain.Audit;

namespace TraceForge.Infrastructure.Persistence;

/// <summary>
/// Single EF Core context for the modular monolith. Identity tables and product tables share one
/// transaction boundary so an account and its audit record can be written atomically where needed.
/// </summary>
public sealed class TraceForgeDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public TraceForgeDbContext(DbContextOptions<TraceForgeDbContext> options)
        : base(options)
    {
    }

    public DbSet<AuditLogEntry> AuditLogs => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(user =>
        {
            user.Property(u => u.DisplayName)
                .IsRequired()
                .HasMaxLength(ApplicationUser.DisplayNameMaxLength);

            user.Property(u => u.CreatedAtUtc).IsRequired();
            user.HasIndex(u => u.CreatedAtUtc);
        });

        builder.Entity<AuditLogEntry>(audit =>
        {
            audit.ToTable("AuditLogs");
            audit.HasKey(a => a.Id);
            audit.Property(a => a.Action).IsRequired().HasMaxLength(AuditLogEntry.ActionMaxLength);
            audit.Property(a => a.ActorIdentifier).HasMaxLength(AuditLogEntry.ActorIdentifierMaxLength);
            audit.Property(a => a.TargetType).IsRequired().HasMaxLength(AuditLogEntry.TargetTypeMaxLength);
            audit.Property(a => a.TargetId).HasMaxLength(AuditLogEntry.TargetIdMaxLength);
            audit.Property(a => a.IpAddress).HasMaxLength(AuditLogEntry.IpAddressMaxLength);
            audit.Property(a => a.UserAgent).HasMaxLength(AuditLogEntry.UserAgentMaxLength);
            audit.Property(a => a.Details).HasMaxLength(AuditLogEntry.DetailsMaxLength);
            audit.Property(a => a.Outcome)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            audit.Property(a => a.OccurredAtUtc).IsRequired();

            audit.HasIndex(a => a.OccurredAtUtc);
            audit.HasIndex(a => a.Action);
            audit.HasIndex(a => a.ActorUserId);
        });

        // SQLite cannot ORDER BY DateTimeOffset columns. Persist them as Unix milliseconds for the
        // SQLite provider so ordering and range queries stay in SQL and remain timezone-safe.
        // PostgreSQL keeps native timestamptz semantics.
        if (Database.IsSqlite())
        {
            ApplySqliteTimestampConversion(builder);
        }
    }

    private static void ApplySqliteTimestampConversion(ModelBuilder builder)
    {
        var nonNullable = new ValueConverter<DateTimeOffset, long>(
            value => value.ToUnixTimeMilliseconds(),
            value => DateTimeOffset.FromUnixTimeMilliseconds(value));

        var nullable = new ValueConverter<DateTimeOffset?, long?>(
            value => value.HasValue ? value.Value.ToUnixTimeMilliseconds() : (long?)null,
            value => value.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(value.Value) : (DateTimeOffset?)null);

        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset))
                {
                    property.SetValueConverter(nonNullable);
                }
                else if (property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(nullable);
                }
            }
        }
    }
}
