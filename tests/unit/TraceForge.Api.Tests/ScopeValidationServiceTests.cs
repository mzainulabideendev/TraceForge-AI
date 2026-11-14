using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TraceForge.Application.Scopes;
using TraceForge.Infrastructure.Scopes;

namespace TraceForge.Api.Tests;

/// <summary>
/// Exercises the deterministic policy directly. DNS is always a controlled stub; no real name
/// resolution or network connection is performed.
/// </summary>
public sealed class ScopeValidationServiceTests : ApiTestBase
{
    private static readonly string[] PrivateNetworkRestriction = ["private-network"];
    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        return await ApiTestClient.ReadJsonAsync(response);
    }

    private async Task<(ApiTestClient Owner, Guid UserId, Guid WorkspaceId)> CreateOwnerAsync(string email)
    {
        var owner = new ApiTestClient(Factory);
        await owner.RegisterAndAuthenticateAsync(email, email);

        using var meResponse = await owner.GetAsync("/api/auth/me");
        var me = await ReadAsync(meResponse, HttpStatusCode.OK);

        using var createResponse = await owner.PostJsonAsync("/api/workspaces", new { name = "Validation", description = (string?)null });
        var workspace = await ReadAsync(createResponse, HttpStatusCode.Created);

        return (owner, me.GetProperty("id").GetGuid(), workspace.GetProperty("id").GetGuid());
    }

    private static async Task<Guid> RegisterAndApproveAsync(
        ApiTestClient owner,
        Guid workspaceId,
        object payload,
        bool approve = true)
    {
        using var register = await owner.PostJsonAsync($"/api/workspaces/{workspaceId}/scopes", payload);
        var scope = await ReadAsync(register, HttpStatusCode.Created);
        var scopeId = scope.GetProperty("id").GetGuid();

        if (approve)
        {
            using var approval = await owner.PostJsonAsync(
                $"/api/workspaces/{workspaceId}/scopes/{scopeId}/approve",
                new { approvalReference = "CHANGE-VALIDATION" });
            await ReadAsync(approval, HttpStatusCode.OK);
        }

        return scopeId;
    }

    private Task<ScopeEvaluation> EvaluateAsync(
        Guid userId,
        Guid workspaceId,
        string target,
        IDnsResolver dns,
        bool allowPrivateNetworks = false,
        string? scheme = null,
        int? port = null,
        DateTimeOffset? now = null)
    {
        return Factory.WithDatabaseAsync(async db =>
        {
            var service = new ScopeValidationService(
                db,
                dns,
                new StubCurrentUser(userId),
                Options.Create(new ScopeValidationOptions { AllowPrivateNetworks = allowPrivateNetworks }),
                new ManualTimeProvider(now ?? DateTimeOffset.UtcNow),
                NullLogger<ScopeValidationService>.Instance);

            return await service.EvaluateAsync(
                workspaceId,
                new ScopeCheckRequest(target, scheme, port),
                CancellationToken.None);
        });
    }

    private Task<ScopeEvaluation> EvaluateRedirectAsync(
        Guid userId,
        Guid workspaceId,
        string originalTarget,
        string redirect,
        IDnsResolver dns)
    {
        return Factory.WithDatabaseAsync(async db =>
        {
            var service = new ScopeValidationService(
                db,
                dns,
                new StubCurrentUser(userId),
                Options.Create(new ScopeValidationOptions()),
                new ManualTimeProvider(DateTimeOffset.UtcNow),
                NullLogger<ScopeValidationService>.Instance);

            return await service.EvaluateRedirectAsync(
                workspaceId,
                new ScopeCheckRequest(originalTarget),
                new Uri(redirect),
                CancellationToken.None);
        });
    }

    [Fact]
    public async Task CidrMembership_AllowsInsideAndDeniesOutside()
    {
        var (owner, userId, workspaceId) = await CreateOwnerAsync("validation-cidr@example.test");
        await RegisterAndApproveAsync(owner, workspaceId, new { type = "Cidr", target = "8.8.8.0/24" });

        var inside = await EvaluateAsync(userId, workspaceId, "8.8.8.200", new StubDnsResolver());
        Assert.True(inside.Allowed, inside.Detail);
        Assert.Equal(IPAddress.Parse("8.8.8.200"), inside.PinnedAddresses[0]);

        var outside = await EvaluateAsync(userId, workspaceId, "8.8.9.1", new StubDnsResolver());
        Assert.False(outside.Allowed);
        Assert.Equal(ScopeDenialReason.NoApprovedScope, outside.Reason);
    }

    [Fact]
    public async Task IPv6_AllowsApprovedAndDeniesOtherAddresses()
    {
        var (owner, userId, workspaceId) = await CreateOwnerAsync("validation-ipv6@example.test");
        await RegisterAndApproveAsync(owner, workspaceId, new { type = "IPv6", target = "2606:4700:4700::1111" });

        var allowed = await EvaluateAsync(userId, workspaceId, "2606:4700:4700::1111", new StubDnsResolver());
        Assert.True(allowed.Allowed, allowed.Detail);

        var denied = await EvaluateAsync(userId, workspaceId, "2606:4700:4700::1001", new StubDnsResolver());
        Assert.False(denied.Allowed);
        Assert.Equal(ScopeDenialReason.NoApprovedScope, denied.Reason);
    }

    [Fact]
    public async Task HostnameApproval_DoesNotAutomaticallyApproveResolvedAddresses()
    {
        var (owner, userId, workspaceId) = await CreateOwnerAsync("validation-hostname@example.test");
        await RegisterAndApproveAsync(owner, workspaceId, new { type = "Hostname", target = "example.com" });

        var dns = new StubDnsResolver();
        dns.Enqueue(IPAddress.Parse("8.8.8.8"));

        var withoutIpScope = await EvaluateAsync(userId, workspaceId, "example.com", dns);
        Assert.False(withoutIpScope.Allowed);
        Assert.Equal(ScopeDenialReason.ResolvedAddressNotApproved, withoutIpScope.Reason);

        await RegisterAndApproveAsync(owner, workspaceId, new { type = "IPv4", target = "8.8.8.8" });

        var dnsAgain = new StubDnsResolver();
        dnsAgain.Enqueue(IPAddress.Parse("8.8.8.8"));

        var withIpScope = await EvaluateAsync(userId, workspaceId, "example.com", dnsAgain);
        Assert.True(withIpScope.Allowed, withIpScope.Detail);
        Assert.Equal(IPAddress.Parse("8.8.8.8"), withIpScope.PinnedAddresses[0]);
    }

    [Fact]
    public async Task DnsRebinding_ToUnapprovedAddressIsDenied()
    {
        var (owner, userId, workspaceId) = await CreateOwnerAsync("validation-rebinding@example.test");
        await RegisterAndApproveAsync(owner, workspaceId, new { type = "Hostname", target = "rebind.example.com" });
        await RegisterAndApproveAsync(owner, workspaceId, new { type = "IPv4", target = "8.8.8.8" });

        var first = new StubDnsResolver();
        first.Enqueue(IPAddress.Parse("8.8.8.8"));
        var allowed = await EvaluateAsync(userId, workspaceId, "rebind.example.com", first);
        Assert.True(allowed.Allowed, allowed.Detail);
        Assert.Equal(IPAddress.Parse("8.8.8.8"), allowed.PinnedAddresses[0]);

        // The name now resolves to a different, unapproved address: the approval must not follow it.
        var second = new StubDnsResolver();
        second.Enqueue(IPAddress.Parse("8.8.4.4"));
        var denied = await EvaluateAsync(userId, workspaceId, "rebind.example.com", second);
        Assert.False(denied.Allowed);
        Assert.Equal(ScopeDenialReason.ResolvedAddressNotApproved, denied.Reason);
        Assert.Empty(denied.PinnedAddresses);
    }

    [Fact]
    public async Task DnsFailure_IsDenied()
    {
        var (owner, userId, workspaceId) = await CreateOwnerAsync("validation-dns-failure@example.test");
        await RegisterAndApproveAsync(owner, workspaceId, new { type = "Hostname", target = "missing.example.com" });

        var evaluation = await EvaluateAsync(userId, workspaceId, "missing.example.com", new StubDnsResolver());

        Assert.False(evaluation.Allowed);
        Assert.Equal(ScopeDenialReason.DnsResolutionFailed, evaluation.Reason);
    }

    [Fact]
    public async Task PrivateNetwork_RequiresGlobalSwitchAndScopeRestriction()
    {
        var (owner, userId, workspaceId) = await CreateOwnerAsync("validation-private@example.test");
        await RegisterAndApproveAsync(
            owner,
            workspaceId,
            new { type = "IPv4", target = "10.0.0.5", restrictions = PrivateNetworkRestriction });

        var blockedByDefault = await EvaluateAsync(userId, workspaceId, "10.0.0.5", new StubDnsResolver());
        Assert.False(blockedByDefault.Allowed);
        Assert.Equal(ScopeDenialReason.PrivateNetworkBlocked, blockedByDefault.Reason);

        var allowedInLab = await EvaluateAsync(
            userId,
            workspaceId,
            "10.0.0.5",
            new StubDnsResolver(),
            allowPrivateNetworks: true);
        Assert.True(allowedInLab.Allowed, allowedInLab.Detail);
        Assert.Equal(IPAddress.Parse("10.0.0.5"), allowedInLab.PinnedAddresses[0]);

        // Without the restriction, even the global lab switch does not permit a private address.
        await RegisterAndApproveAsync(owner, workspaceId, new { type = "IPv4", target = "10.0.0.6" });
        var missingRestriction = await EvaluateAsync(
            userId,
            workspaceId,
            "10.0.0.6",
            new StubDnsResolver(),
            allowPrivateNetworks: true);

        Assert.False(missingRestriction.Allowed);
        Assert.Equal(ScopeDenialReason.PrivateNetworkBlocked, missingRestriction.Reason);
    }

    [Fact]
    public async Task Origin_RequiresSchemeHostPortAndAddressApproval()
    {
        var (owner, userId, workspaceId) = await CreateOwnerAsync("validation-origin@example.test");
        await RegisterAndApproveAsync(owner, workspaceId, new { type = "Origin", target = "https://example.com" });
        await RegisterAndApproveAsync(owner, workspaceId, new { type = "Hostname", target = "example.com" });
        await RegisterAndApproveAsync(owner, workspaceId, new { type = "IPv4", target = "8.8.8.8" });

        var dns = new StubDnsResolver();
        dns.Enqueue(IPAddress.Parse("8.8.8.8"));
        var allowed = await EvaluateAsync(userId, workspaceId, "https://example.com", dns);
        Assert.True(allowed.Allowed, allowed.Detail);
        Assert.Equal(IPAddress.Parse("8.8.8.8"), allowed.PinnedAddresses[0]);

        var wrongPort = await EvaluateAsync(userId, workspaceId, "https://example.com:8443", new StubDnsResolver());
        Assert.False(wrongPort.Allowed);
        Assert.Equal(ScopeDenialReason.NoApprovedScope, wrongPort.Reason);

        var wrongScheme = await EvaluateAsync(userId, workspaceId, "http://example.com", new StubDnsResolver());
        Assert.False(wrongScheme.Allowed);

        var userInfo = await EvaluateAsync(userId, workspaceId, "https://user:secret@example.com", new StubDnsResolver());
        Assert.False(userInfo.Allowed);
        Assert.Equal(ScopeDenialReason.MalformedTarget, userInfo.Reason);
    }

    [Fact]
    public async Task Redirect_WithinOriginIsAllowedButOutsideIsDenied()
    {
        var (owner, userId, workspaceId) = await CreateOwnerAsync("validation-redirect@example.test");
        await RegisterAndApproveAsync(owner, workspaceId, new { type = "Origin", target = "https://example.com" });
        await RegisterAndApproveAsync(owner, workspaceId, new { type = "Hostname", target = "example.com" });
        await RegisterAndApproveAsync(owner, workspaceId, new { type = "IPv4", target = "8.8.8.8" });

        var sameOriginDns = new StubDnsResolver();
        sameOriginDns.Enqueue(IPAddress.Parse("8.8.8.8"));
        var sameOrigin = await EvaluateRedirectAsync(
            userId,
            workspaceId,
            "https://example.com",
            "https://example.com/login",
            sameOriginDns);
        Assert.True(sameOrigin.Allowed, sameOrigin.Detail);

        var otherHost = await EvaluateRedirectAsync(
            userId,
            workspaceId,
            "https://example.com",
            "https://evil.example.net/login",
            new StubDnsResolver());
        Assert.False(otherHost.Allowed);
        Assert.Equal(ScopeDenialReason.RedirectOutsideScope, otherHost.Reason);

        var otherPort = await EvaluateRedirectAsync(
            userId,
            workspaceId,
            "https://example.com",
            "https://example.com:8443/login",
            new StubDnsResolver());
        Assert.False(otherPort.Allowed);
        Assert.Equal(ScopeDenialReason.RedirectOutsideScope, otherPort.Reason);

        var otherScheme = await EvaluateRedirectAsync(
            userId,
            workspaceId,
            "https://example.com",
            "http://example.com/login",
            new StubDnsResolver());
        Assert.False(otherScheme.Allowed);
        Assert.Equal(ScopeDenialReason.RedirectOutsideScope, otherScheme.Reason);
    }

    [Fact]
    public async Task NonMember_IsDeniedEvenWithApprovedScopes()
    {
        var (owner, _, workspaceId) = await CreateOwnerAsync("validation-nonmember@example.test");
        await RegisterAndApproveAsync(owner, workspaceId, new { type = "IPv4", target = "8.8.8.8" });

        var evaluation = await EvaluateAsync(Guid.NewGuid(), workspaceId, "8.8.8.8", new StubDnsResolver());

        Assert.False(evaluation.Allowed);
        Assert.Equal(ScopeDenialReason.NoApprovedScope, evaluation.Reason);
    }

    [Fact]
    public async Task RevokedScope_IsDeniedWithRevokedReason()
    {
        var (owner, userId, workspaceId) = await CreateOwnerAsync("validation-revoked@example.test");
        var scopeId = await RegisterAndApproveAsync(owner, workspaceId, new { type = "IPv4", target = "1.1.1.1" });

        using var revoke = await owner.PostJsonAsync(
            $"/api/workspaces/{workspaceId}/scopes/{scopeId}/revoke",
            new { reason = "compromised" });
        await ReadAsync(revoke, HttpStatusCode.OK);

        var evaluation = await EvaluateAsync(userId, workspaceId, "1.1.1.1", new StubDnsResolver());

        Assert.False(evaluation.Allowed);
        Assert.Equal(ScopeDenialReason.ScopeRevoked, evaluation.Reason);
    }

    [Fact]
    public async Task ExpiredScope_IsDeniedEvenIfStatusWasNotUpdated()
    {
        var (owner, userId, workspaceId) = await CreateOwnerAsync("validation-expired@example.test");
        var scopeId = await RegisterAndApproveAsync(owner, workspaceId, new { type = "IPv4", target = "1.0.0.1" });

        await Factory.WithDatabaseAsync(async db =>
        {
            var scope = await db.AuthorizedScopes.SingleAsync(s => s.Id == scopeId);
            db.Entry(scope).Property(s => s.ExpiresAtUtc).CurrentValue = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
            return true;
        });

        var evaluation = await EvaluateAsync(userId, workspaceId, "1.0.0.1", new StubDnsResolver());

        Assert.False(evaluation.Allowed);
        Assert.Equal(ScopeDenialReason.ScopeExpired, evaluation.Reason);
    }

    [Fact]
    public async Task PendingScope_IsDeniedWithPendingReason()
    {
        var (owner, userId, workspaceId) = await CreateOwnerAsync("validation-pending@example.test");
        await RegisterAndApproveAsync(owner, workspaceId, new { type = "IPv4", target = "4.4.4.4" }, approve: false);

        var evaluation = await EvaluateAsync(userId, workspaceId, "4.4.4.4", new StubDnsResolver());

        Assert.False(evaluation.Allowed);
        Assert.Equal(ScopeDenialReason.ScopePending, evaluation.Reason);
    }
}
