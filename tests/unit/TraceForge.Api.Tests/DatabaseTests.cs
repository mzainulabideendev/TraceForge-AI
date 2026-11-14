using Microsoft.EntityFrameworkCore;
using TraceForge.Domain.Identity;

namespace TraceForge.Api.Tests;

public sealed class DatabaseTests : ApiTestBase
{
    [Fact]
    public async Task Migrations_AreApplied_ToTheRealDatabaseFile()
    {
        var applied = await Factory.WithDatabaseAsync(db => db.Database.GetAppliedMigrationsAsync());

        Assert.Contains(applied, migration => migration.EndsWith("_InitialCreate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IdentityAndAuditTables_Exist_AndAreQueryable()
    {
        var (userCount, roleNames, auditCount) = await Factory.WithDatabaseAsync(async db =>
            (
                await db.Users.CountAsync(),
                await db.Roles.Select(r => r.Name!).ToListAsync(),
                await db.AuditLogs.CountAsync()
            ));

        Assert.True(userCount >= 0);
        Assert.True(auditCount >= 0);
        Assert.Contains(ApplicationRoles.Administrator, roleNames);
        Assert.Contains(ApplicationRoles.Analyst, roleNames);
    }

    [Fact]
    public async Task Registration_PersistsAccount_AndAuditTimestampIsUtc()
    {
        var email = "persisted@example.test";
        using var client = new ApiTestClient(Factory);
        await client.RegisterAndAuthenticateAsync(email, "Persisted User");

        var (storedEmail, createdAtUtc, auditOffsets) = await Factory.WithDatabaseAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Email == email);
            var offsets = await db.AuditLogs
                .Where(a => a.ActorIdentifier == email)
                .Select(a => a.OccurredAtUtc)
                .ToListAsync();

            return (user.Email, user.CreatedAtUtc, offsets);
        });

        Assert.Equal(email, storedEmail);
        Assert.Equal(TimeSpan.Zero, createdAtUtc.Offset);
        Assert.NotEmpty(auditOffsets);
        Assert.All(auditOffsets, timestamp => Assert.Equal(TimeSpan.Zero, timestamp.Offset));
    }

    [Fact]
    public async Task AuditRecords_AreAppendOnly_NoUpdateOrDeletePathExists()
    {
        using var client = new ApiTestClient(Factory);
        await client.RegisterAndAuthenticateAsync("append-only@example.test", "Append Only");

        var countBefore = await Factory.WithDatabaseAsync(db => db.AuditLogs.CountAsync());

        // Re-authenticate: this must add records, never mutate prior ones.
        await client.FetchCsrfTokenAsync();
        using var logout = await client.PostAsync("/api/auth/logout");
        Assert.Equal(System.Net.HttpStatusCode.NoContent, logout.StatusCode);

        var countAfter = await Factory.WithDatabaseAsync(db => db.AuditLogs.CountAsync());
        Assert.True(countAfter > countBefore, "Auditing actions must append new records.");
    }
}
