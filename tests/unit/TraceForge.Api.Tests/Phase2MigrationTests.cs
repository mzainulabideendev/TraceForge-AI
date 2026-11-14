using Microsoft.EntityFrameworkCore;
using TraceForge.Domain.Workspaces;

namespace TraceForge.Api.Tests;

public sealed class Phase2MigrationTests : ApiTestBase
{
    [Fact]
    public async Task SqliteMigrations_ApplyInitialAndPhase2()
    {
        var applied = await Factory.WithDatabaseAsync(db => db.Database.GetAppliedMigrationsAsync());

        var names = applied.ToArray();
        Assert.Contains(names, name => name.Contains("InitialCreate", StringComparison.Ordinal));
        Assert.Contains(names, name => name.Contains("Phase2Workspaces", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Phase2Tables_AreQueryable()
    {
        var counts = await Factory.WithDatabaseAsync(async db => new
        {
            Workspaces = await db.Workspaces.CountAsync(),
            Memberships = await db.WorkspaceMemberships.CountAsync(),
            Scopes = await db.AuthorizedScopes.CountAsync(),
            Approvals = await db.ScopeApprovals.CountAsync(),
            ScopeAudit = await db.ScopeAuditEvents.CountAsync(),
        });

        Assert.Equal(0, counts.Workspaces);
        Assert.Equal(0, counts.Memberships);
        Assert.Equal(0, counts.Scopes);
        Assert.Equal(0, counts.Approvals);
        Assert.Equal(0, counts.ScopeAudit);
    }

    [Fact]
    public async Task UniqueMembershipIndex_IsEnforcedByDatabase()
    {
        using var owner = new ApiTestClient(Factory);
        await owner.RegisterAndAuthenticateAsync("migration-owner@example.test", "Migration Owner");

        using var createResponse = await owner.PostJsonAsync("/api/workspaces", new { name = "Database", description = (string?)null });
        Assert.Equal(System.Net.HttpStatusCode.Created, createResponse.StatusCode);
        var workspace = await ApiTestClient.ReadJsonAsync(createResponse);
        var workspaceId = workspace.GetProperty("id").GetGuid();

        var exception = await Assert.ThrowsAsync<DbUpdateException>(async () =>
            await Factory.WithDatabaseAsync(async db =>
            {
                var membership = await db.WorkspaceMemberships.SingleAsync(m => m.WorkspaceId == workspaceId);
                db.WorkspaceMemberships.Add(WorkspaceMembership.Create(
                    workspaceId,
                    membership.UserId,
                    WorkspaceRole.Analyst,
                    DateTimeOffset.UtcNow));
                await db.SaveChangesAsync();
                return true;
            }));

        Assert.Contains("UNIQUE", exception.InnerException?.Message ?? exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeletingWorkspace_CascadesScopes()
    {
        using var owner = new ApiTestClient(Factory);
        await owner.RegisterAndAuthenticateAsync("migration-cascade@example.test", "Migration Cascade");

        using var createResponse = await owner.PostJsonAsync("/api/workspaces", new { name = "Cascade", description = (string?)null });
        var workspace = await ApiTestClient.ReadJsonAsync(createResponse);
        var workspaceId = workspace.GetProperty("id").GetGuid();

        using (var scopeResponse = await owner.PostJsonAsync(
                   $"/api/workspaces/{workspaceId}/scopes",
                   new { type = "IPv4", target = "8.8.8.8" }))
        {
            Assert.Equal(System.Net.HttpStatusCode.Created, scopeResponse.StatusCode);
        }

        await Factory.WithDatabaseAsync(async db =>
        {
            var entity = await db.Workspaces.SingleAsync(w => w.Id == workspaceId);
            db.Workspaces.Remove(entity);
            await db.SaveChangesAsync();
            return true;
        });

        var remaining = await Factory.WithDatabaseAsync(async db => new
        {
            Scopes = await db.AuthorizedScopes.CountAsync(s => s.WorkspaceId == workspaceId),
            Memberships = await db.WorkspaceMemberships.CountAsync(m => m.WorkspaceId == workspaceId),
        });

        Assert.Equal(0, remaining.Scopes);
        Assert.Equal(0, remaining.Memberships);
    }
}
