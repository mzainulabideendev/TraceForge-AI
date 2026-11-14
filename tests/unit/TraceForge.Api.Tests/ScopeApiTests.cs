using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TraceForge.Domain.Audit;

namespace TraceForge.Api.Tests;

public sealed class ScopeApiTests : ApiTestBase
{
    private static readonly string[] PrivateNetworkRestriction = ["private-network"];
    private static readonly string[] UnknownRestriction = ["wildcard"];
    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        return await ApiTestClient.ReadJsonAsync(response);
    }

    private static async Task<Guid> CreateWorkspaceAsync(ApiTestClient client, string name)
    {
        using var response = await client.PostJsonAsync("/api/workspaces", new { name, description = (string?)null });
        var json = await ReadAsync(response, HttpStatusCode.Created);
        return json.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> RegisterScopeAsync(
        ApiTestClient client,
        Guid workspaceId,
        object payload,
        HttpStatusCode expected = HttpStatusCode.Created)
    {
        using var response = await client.PostJsonAsync($"/api/workspaces/{workspaceId}/scopes", payload);
        return await ReadAsync(response, expected);
    }

    private static async Task<JsonElement> ApproveAsync(ApiTestClient client, Guid workspaceId, Guid scopeId, string reference)
    {
        using var response = await client.PostJsonAsync(
            $"/api/workspaces/{workspaceId}/scopes/{scopeId}/approve",
            new { approvalReference = reference });

        return await ReadAsync(response, HttpStatusCode.OK);
    }

    private static async Task<(ApiTestClient Owner, Guid WorkspaceId)> CreateOwnerWithWorkspaceAsync(
        TraceForgeApiFactory factory,
        string email,
        string name)
    {
        var owner = new ApiTestClient(factory);
        await owner.RegisterAndAuthenticateAsync(email, name);
        var workspaceId = await CreateWorkspaceAsync(owner, name);
        return (owner, workspaceId);
    }

    [Fact]
    public async Task ScopeLifecycle_RegisterApproveRevoke()
    {
        var (owner, workspaceId) = await CreateOwnerWithWorkspaceAsync(Factory, "scope-owner@example.test", "Scope Lifecycle");

        var registered = await RegisterScopeAsync(owner, workspaceId, new { type = "IPv4", target = "8.8.8.8" });
        var scopeId = registered.GetProperty("id").GetGuid();

        Assert.Equal("Pending", registered.GetProperty("status").GetString());
        Assert.False(registered.GetProperty("currentlyUsable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, registered.GetProperty("approvedAtUtc").ValueKind);

        var approved = await ApproveAsync(owner, workspaceId, scopeId, "CHANGE-1234");
        Assert.Equal("Approved", approved.GetProperty("status").GetString());
        Assert.Equal("CHANGE-1234", approved.GetProperty("approvalReference").GetString());
        Assert.True(approved.GetProperty("currentlyUsable").GetBoolean());

        using var revokeResponse = await owner.PostJsonAsync(
            $"/api/workspaces/{workspaceId}/scopes/{scopeId}/revoke",
            new { reason = "no longer needed" });
        var revoked = await ReadAsync(revokeResponse, HttpStatusCode.OK);
        Assert.Equal("Revoked", revoked.GetProperty("status").GetString());
        Assert.False(revoked.GetProperty("currentlyUsable").GetBoolean());

        using var reapproveResponse = await owner.PostJsonAsync(
            $"/api/workspaces/{workspaceId}/scopes/{scopeId}/approve",
            new { approvalReference = "CHANGE-5678" });
        Assert.Equal(HttpStatusCode.Conflict, reapproveResponse.StatusCode);
    }

    [Fact]
    public async Task Approve_RequiresEvidenceAndAnApprovingRole()
    {
        var (owner, workspaceId) = await CreateOwnerWithWorkspaceAsync(Factory, "scope-evidence@example.test", "Scope Evidence");

        using var analyst = new ApiTestClient(Factory);
        await analyst.RegisterAndAuthenticateAsync("scope-analyst@example.test", "Scope Analyst");
        using (var addResponse = await owner.PostJsonAsync(
                   $"/api/workspaces/{workspaceId}/members",
                   new { email = "scope-analyst@example.test", role = "Analyst" }))
        {
            await ReadAsync(addResponse, HttpStatusCode.Created);
        }

        var registered = await RegisterScopeAsync(analyst, workspaceId, new { type = "IPv4", target = "1.1.1.1" });
        var scopeId = registered.GetProperty("id").GetGuid();
        Assert.Equal("Pending", registered.GetProperty("status").GetString());

        using (var analystApprove = await analyst.PostJsonAsync(
                   $"/api/workspaces/{workspaceId}/scopes/{scopeId}/approve",
                   new { approvalReference = "CHANGE-1" }))
        {
            Assert.Equal(HttpStatusCode.Forbidden, analystApprove.StatusCode);
        }

        using (var emptyEvidence = await owner.PostJsonAsync(
                   $"/api/workspaces/{workspaceId}/scopes/{scopeId}/approve",
                   new { approvalReference = " " }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, emptyEvidence.StatusCode);
        }

        var approved = await ApproveAsync(owner, workspaceId, scopeId, "CHANGE-2");
        Assert.Equal("Approved", approved.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ScopeRegistration_NormalizesAndRejectsMalformedTargets()
    {
        var (owner, workspaceId) = await CreateOwnerWithWorkspaceAsync(Factory, "scope-normalize@example.test", "Scope Normalize");

        var cidr = await RegisterScopeAsync(owner, workspaceId, new { type = "Cidr", target = "192.168.1.130/24" });
        Assert.Equal("192.168.1.0/24", cidr.GetProperty("target").GetString());

        var origin = await RegisterScopeAsync(owner, workspaceId, new { type = "Origin", target = "HTTPS://Example.COM/" });
        Assert.Equal("https://example.com:443", origin.GetProperty("target").GetString());

        await RegisterScopeAsync(owner, workspaceId, new { type = "IPv4", target = "not-an-ip" }, HttpStatusCode.BadRequest);
        await RegisterScopeAsync(owner, workspaceId, new { type = "Origin", target = "https://example.com/path" }, HttpStatusCode.BadRequest);
        await RegisterScopeAsync(owner, workspaceId, new { type = "Cidr", target = "8.8.8.0/0" }, HttpStatusCode.BadRequest);
        await RegisterScopeAsync(owner, workspaceId, new { type = "IPv6", target = "::ffff:8.8.8.8" }, HttpStatusCode.BadRequest);
        await RegisterScopeAsync(owner, workspaceId, new { type = "Nope", target = "8.8.8.8" }, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DuplicateScope_IsRejectedAfterNormalization()
    {
        var (owner, workspaceId) = await CreateOwnerWithWorkspaceAsync(Factory, "scope-duplicate@example.test", "Scope Duplicate");

        await RegisterScopeAsync(owner, workspaceId, new { type = "Hostname", target = "Example.com." });
        await RegisterScopeAsync(owner, workspaceId, new { type = "Hostname", target = "example.COM" }, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ScopeExpiration_IsEnforced()
    {
        var (owner, workspaceId) = await CreateOwnerWithWorkspaceAsync(Factory, "scope-expiry@example.test", "Scope Expiry");

        var registered = await RegisterScopeAsync(owner, workspaceId, new { type = "IPv4", target = "9.9.9.9" });
        var scopeId = registered.GetProperty("id").GetGuid();
        await ApproveAsync(owner, workspaceId, scopeId, "CHANGE-EXPIRY");

        // Move the stored deadline into the past, as would happen with the passage of real time.
        await Factory.WithDatabaseAsync(async db =>
        {
            var scope = await db.AuthorizedScopes.SingleAsync(s => s.Id == scopeId);
            db.Entry(scope).Property(s => s.ExpiresAtUtc).CurrentValue = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
            return true;
        });

        using var expireResponse = await owner.PostJsonAsync($"/api/workspaces/{workspaceId}/scopes/{scopeId}/expire", new { });
        var expired = await ReadAsync(expireResponse, HttpStatusCode.OK);
        Assert.Equal("Expired", expired.GetProperty("status").GetString());
        Assert.False(expired.GetProperty("currentlyUsable").GetBoolean());

        using var secondExpire = await owner.PostJsonAsync($"/api/workspaces/{workspaceId}/scopes/{scopeId}/expire", new { });
        await ReadAsync(secondExpire, HttpStatusCode.OK);
    }

    [Fact]
    public async Task Expire_BeforeDeadline_ReturnsConflict()
    {
        var (owner, workspaceId) = await CreateOwnerWithWorkspaceAsync(Factory, "scope-early-expiry@example.test", "Scope Early Expiry");

        var registered = await RegisterScopeAsync(owner, workspaceId, new { type = "IPv4", target = "9.9.9.10" });
        var scopeId = registered.GetProperty("id").GetGuid();

        using var response = await owner.PostJsonAsync($"/api/workspaces/{workspaceId}/scopes/{scopeId}/expire", new { });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RestrictionChange_ResetsApproval()
    {
        var (owner, workspaceId) = await CreateOwnerWithWorkspaceAsync(Factory, "scope-restrictions@example.test", "Scope Restrictions");

        var registered = await RegisterScopeAsync(owner, workspaceId, new { type = "IPv4", target = "10.0.0.5" });
        var scopeId = registered.GetProperty("id").GetGuid();
        await ApproveAsync(owner, workspaceId, scopeId, "LAB-1234");

        using (var response = await owner.PatchJsonAsync(
                   $"/api/workspaces/{workspaceId}/scopes/{scopeId}/restrictions",
                   new { restrictions = PrivateNetworkRestriction }))
        {
            var updated = await ReadAsync(response, HttpStatusCode.OK);
            Assert.Equal("Pending", updated.GetProperty("status").GetString());
            Assert.Null(updated.GetProperty("approvedAtUtc").GetString());
            Assert.Single(updated.GetProperty("restrictions").EnumerateArray());
        }

        using (var invalid = await owner.PatchJsonAsync(
                   $"/api/workspaces/{workspaceId}/scopes/{scopeId}/restrictions",
                   new { restrictions = UnknownRestriction }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
    }

    [Fact]
    public async Task ScopeEndpoints_EnforceWorkspaceIsolationAndMembership()
    {
        var (owner, workspaceId) = await CreateOwnerWithWorkspaceAsync(Factory, "scope-isolation@example.test", "Scope Isolation");
        var registered = await RegisterScopeAsync(owner, workspaceId, new { type = "IPv4", target = "8.8.4.4" });
        var scopeId = registered.GetProperty("id").GetGuid();

        using var outsider = new ApiTestClient(Factory);
        await outsider.RegisterAndAuthenticateAsync("scope-outsider@example.test", "Scope Outsider");

        using var listResponse = await outsider.GetAsync($"/api/workspaces/{workspaceId}/scopes");
        Assert.Equal(HttpStatusCode.NotFound, listResponse.StatusCode);

        using var getResponse = await outsider.GetAsync($"/api/workspaces/{workspaceId}/scopes/{scopeId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        using var registerResponse = await outsider.PostJsonAsync(
            $"/api/workspaces/{workspaceId}/scopes",
            new { type = "IPv4", target = "8.8.4.5" });
        Assert.Equal(HttpStatusCode.NotFound, registerResponse.StatusCode);

        // A member of another workspace cannot reach this scope through their own workspace id.
        var (otherOwner, otherWorkspaceId) = await CreateOwnerWithWorkspaceAsync(Factory, "scope-other@example.test", "Scope Other");
        using var crossResponse = await otherOwner.GetAsync($"/api/workspaces/{otherWorkspaceId}/scopes/{scopeId}");
        Assert.Equal(HttpStatusCode.NotFound, crossResponse.StatusCode);
    }

    [Fact]
    public async Task Evaluate_AllowsApprovedAddressAndDeniesPending()
    {
        var (owner, workspaceId) = await CreateOwnerWithWorkspaceAsync(Factory, "scope-evaluate@example.test", "Scope Evaluate");

        var pending = await RegisterScopeAsync(owner, workspaceId, new { type = "IPv4", target = "8.8.4.4" });
        var pendingId = pending.GetProperty("id").GetGuid();

        using (var pendingResponse = await owner.PostJsonAsync(
                   $"/api/workspaces/{workspaceId}/scopes/evaluate",
                   new { target = "8.8.4.4" }))
        {
            var evaluation = await ReadAsync(pendingResponse, HttpStatusCode.OK);
            Assert.False(evaluation.GetProperty("allowed").GetBoolean());
            Assert.Equal("ScopePending", evaluation.GetProperty("reason").GetString());
        }

        await ApproveAsync(owner, workspaceId, pendingId, "CHANGE-EVAL");

        using (var allowedResponse = await owner.PostJsonAsync(
                   $"/api/workspaces/{workspaceId}/scopes/evaluate",
                   new { target = "8.8.4.4" }))
        {
            var evaluation = await ReadAsync(allowedResponse, HttpStatusCode.OK);
            Assert.True(evaluation.GetProperty("allowed").GetBoolean());
            Assert.Equal(pendingId, evaluation.GetProperty("matchedScopeId").GetGuid());
            Assert.Equal("8.8.4.4", evaluation.GetProperty("pinnedAddresses")[0].GetString());
        }

        using (var deniedResponse = await owner.PostJsonAsync(
                   $"/api/workspaces/{workspaceId}/scopes/evaluate",
                   new { target = "8.8.4.5" }))
        {
            var evaluation = await ApiTestClient.ReadJsonAsync(deniedResponse);
            Assert.False(evaluation.GetProperty("allowed").GetBoolean());
            Assert.Equal("NoApprovedScope", evaluation.GetProperty("reason").GetString());
        }
    }

    [Fact]
    public async Task ScopeLifecycle_IsRecordedInScopeAuditHistory()
    {
        var (owner, workspaceId) = await CreateOwnerWithWorkspaceAsync(Factory, "scope-history@example.test", "Scope History");
        var registered = await RegisterScopeAsync(owner, workspaceId, new { type = "IPv4", target = "8.8.8.8" });
        var scopeId = registered.GetProperty("id").GetGuid();
        await ApproveAsync(owner, workspaceId, scopeId, "CHANGE-HISTORY");

        using var revokeResponse = await owner.PostJsonAsync(
            $"/api/workspaces/{workspaceId}/scopes/{scopeId}/revoke",
            new { reason = "test" });
        await ReadAsync(revokeResponse, HttpStatusCode.OK);

        var actions = await Factory.WithDatabaseAsync(async db =>
            await db.ScopeAuditEvents
                .Where(e => e.ScopeId == scopeId)
                .OrderBy(e => e.Id)
                .Select(e => e.Action)
                .ToListAsync());

        Assert.Equal([AuditActions.ScopeRegistered, AuditActions.ScopeApproved, AuditActions.ScopeRevoked], actions);
    }
}
