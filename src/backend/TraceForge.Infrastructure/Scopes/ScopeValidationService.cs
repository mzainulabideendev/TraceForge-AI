using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TraceForge.Application.Common;
using TraceForge.Application.Scopes;
using TraceForge.Domain.Scopes;
using TraceForge.Domain.Workspaces;
using TraceForge.Infrastructure.Persistence;

namespace TraceForge.Infrastructure.Scopes;

/// <summary>
/// Deterministic scope authorisation. It separates the domain-level approval decision from DNS
/// resolution: proving a hostname is approved never proves that the addresses it currently resolves
/// to are approved. The returned pinned addresses are what a caller must connect to.
/// </summary>
public sealed class ScopeValidationService : IScopeValidationService
{
    private readonly TraceForgeDbContext _dbContext;
    private readonly IDnsResolver _dnsResolver;
    private readonly ICurrentUser _currentUser;
    private readonly ScopeValidationOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ScopeValidationService> _logger;

    public ScopeValidationService(
        TraceForgeDbContext dbContext,
        IDnsResolver dnsResolver,
        ICurrentUser currentUser,
        IOptions<ScopeValidationOptions> options,
        TimeProvider timeProvider,
        ILogger<ScopeValidationService> logger)
    {
        _dbContext = dbContext;
        _dnsResolver = dnsResolver;
        _currentUser = currentUser;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ScopeEvaluation> EvaluateAsync(
        Guid workspaceId,
        ScopeCheckRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Target))
        {
            return ScopeEvaluation.Deny(ScopeDenialReason.MalformedTarget, "A target is required.");
        }

        var authorized = await IsAuthorizedForWorkspaceAsync(workspaceId, cancellationToken);
        if (!authorized)
        {
            return ScopeEvaluation.Deny(ScopeDenialReason.NoApprovedScope, "The caller cannot access this workspace.");
        }

        var scopes = await _dbContext.AuthorizedScopes
            .AsNoTracking()
            .Where(s => s.WorkspaceId == workspaceId)
            .ToListAsync(cancellationToken);

        var now = _timeProvider.GetUtcNow();
        var target = request.Target.Trim();

        if (target.Contains("://", StringComparison.Ordinal) || request.Scheme is not null)
        {
            return await EvaluateOriginAsync(target, request, scopes, now, cancellationToken);
        }

        if (IPAddress.TryParse(target, out var address))
        {
            var (allowed, reason, detail, scope, pinned) = EvaluateAddress(address, scopes, now);
            return allowed
                ? ScopeEvaluation.Allow(scope!.Id, pinned, detail)
                : ScopeEvaluation.Deny(reason, detail, scope?.Id);
        }

        return await EvaluateHostnameAsync(target, scopes, now, cancellationToken);
    }

    public async Task<ScopeEvaluation> EvaluateRedirectAsync(
        Guid workspaceId,
        ScopeCheckRequest originalRequest,
        Uri redirect,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(originalRequest);
        ArgumentNullException.ThrowIfNull(redirect);

        if (!originalRequest.Target.Contains("://", StringComparison.Ordinal) &&
            originalRequest.Scheme is null)
        {
            return ScopeEvaluation.Deny(
                ScopeDenialReason.RedirectOutsideScope,
                "Redirect validation requires an absolute origin for the original request.");
        }

        var originalTarget = originalRequest.Target.Trim();
        if (!originalTarget.Contains("://", StringComparison.Ordinal))
        {
            var port = originalRequest.Port ??
                (string.Equals(originalRequest.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80);
            originalTarget = $"{originalRequest.Scheme}://{originalTarget}:{port}";
        }

        if (!ScopeTargetNormalizer.TryParseOrigin(originalTarget, out var originalOrigin, out _))
        {
            return ScopeEvaluation.Deny(
                ScopeDenialReason.MalformedTarget,
                "The original request is not a valid origin.");
        }

        if (!redirect.IsAbsoluteUri ||
            !ScopeTargetNormalizer.TryParseOrigin(redirect.GetLeftPart(UriPartial.Authority), out var redirectOrigin, out _))
        {
            return ScopeEvaluation.Deny(ScopeDenialReason.MalformedTarget, "The redirect target is not a valid absolute origin.");
        }

        if (!string.Equals(originalOrigin.Scheme, redirectOrigin.Scheme, StringComparison.Ordinal) ||
            !string.Equals(originalOrigin.Host, redirectOrigin.Host, StringComparison.OrdinalIgnoreCase) ||
            originalOrigin.Port != redirectOrigin.Port)
        {
            return ScopeEvaluation.Deny(
                ScopeDenialReason.RedirectOutsideScope,
                "The redirect changes scheme, host or port and is outside the approved origin.");
        }

        return await EvaluateAsync(
            workspaceId,
            new ScopeCheckRequest(redirectOrigin.Value),
            cancellationToken);
    }

    private async Task<ScopeEvaluation> EvaluateOriginAsync(
        string target,
        ScopeCheckRequest request,
        IReadOnlyList<AuthorizedScope> scopes,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        string originText = target;
        if (!originText.Contains("://", StringComparison.Ordinal))
        {
            var port = request.Port ??
                (string.Equals(request.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80);
            originText = $"{request.Scheme}://{originText}:{port}";
        }

        if (!ScopeTargetNormalizer.TryParseOrigin(originText, out var origin, out var originError))
        {
            return ScopeEvaluation.Deny(ScopeDenialReason.MalformedTarget, originError ?? "The origin is invalid.");
        }

        var originMatches = scopes
            .Where(s => s.Type == ScopeType.Origin && s.NormalizedTarget == origin.Value)
            .ToList();

        var originScope = originMatches.FirstOrDefault(s => s.IsUsable(now));
        if (originScope is null)
        {
            return InactiveOrMissing(originMatches, now, Swift.Origin, origin.Value);
        }

        if (origin.IsIpLiteral)
        {
            var (allowed, reason, detail, scope, pinned) =
                EvaluateAddress(IPAddress.Parse(origin.Host), scopes, now);

            return allowed
                ? ScopeEvaluation.Allow(originScope.Id, pinned, detail)
                : ScopeEvaluation.Deny(reason, detail, scope?.Id ?? originScope.Id);
        }

        var hostnameEvaluation = await EvaluateHostnameAsync(origin.Host, scopes, now, cancellationToken);
        if (!hostnameEvaluation.Allowed)
        {
            return hostnameEvaluation;
        }

        // The origin scope proves scheme/host/port. The hostname and every resolved address must
        // still be approved independently.
        return ScopeEvaluation.Allow(originScope.Id, hostnameEvaluation.PinnedAddresses, hostnameEvaluation.Detail);
    }

    private async Task<ScopeEvaluation> EvaluateHostnameAsync(
        string hostname,
        IReadOnlyList<AuthorizedScope> scopes,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!ScopeTargetNormalizer.TryNormalizeHostname(hostname, out var normalized, out var error))
        {
            return ScopeEvaluation.Deny(ScopeDenialReason.MalformedTarget, error ?? "The hostname is invalid.");
        }

        var hostnameMatches = scopes
            .Where(s => s.Type == ScopeType.Hostname && s.NormalizedTarget == normalized)
            .ToList();

        var hostnameScope = hostnameMatches.FirstOrDefault(s => s.IsUsable(now));
        if (hostnameScope is null)
        {
            return InactiveOrMissing(hostnameMatches, now, Swift.Hostname, normalized);
        }

        IReadOnlyList<IPAddress> resolved;
        try
        {
            resolved = await _dnsResolver.ResolveAsync(normalized, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "DNS resolution failed during scope evaluation.");
            return ScopeEvaluation.Deny(
                ScopeDenialReason.DnsResolutionFailed,
                "The hostname could not be resolved.",
                hostnameScope.Id);
        }

        var addresses = resolved
            .Select(ScopeTargetNormalizer.Unmap)
            .Distinct()
            .ToArray();

        if (addresses.Length == 0)
        {
            return ScopeEvaluation.Deny(
                ScopeDenialReason.DnsResolutionFailed,
                "The hostname resolved to no addresses.",
                hostnameScope.Id);
        }

        foreach (var address in addresses)
        {
            var (allowed, reason, detail, _, _) = EvaluateAddress(address, scopes, now);
            if (!allowed)
            {
                // An approved hostname with no matching address scope is an address-authorisation
                // failure, not a missing hostname scope.
                var mapped = reason == ScopeDenialReason.NoApprovedScope
                    ? ScopeDenialReason.ResolvedAddressNotApproved
                    : reason;

                return ScopeEvaluation.Deny(
                    mapped,
                    $"Resolved address {address} is not usable: {detail}",
                    hostnameScope.Id);
            }
        }

        return ScopeEvaluation.Allow(
            hostnameScope.Id,
            addresses,
            $"Hostname and {addresses.Length} resolved address(es) are approved.");
    }

    /// <summary>
    /// Evaluates one destination address against IP and CIDR scopes. The returned pin is the exact
    /// address the caller must connect to.
    /// </summary>
    private (bool Allowed, ScopeDenialReason Reason, string Detail, AuthorizedScope? Scope, IReadOnlyList<IPAddress> Pinned)
        EvaluateAddress(IPAddress candidate, IReadOnlyList<AuthorizedScope> scopes, DateTimeOffset now)
    {
        var address = ScopeTargetNormalizer.Unmap(candidate);
        var pin = new[] { address };

        var matching = new List<AuthorizedScope>();

        foreach (var scope in scopes)
        {
            switch (scope.Type)
            {
                case ScopeType.IPv4:
                case ScopeType.IPv6:
                    if (IPAddress.TryParse(scope.NormalizedTarget, out var ip) &&
                        ScopeTargetNormalizer.Unmap(ip).Equals(address))
                    {
                        matching.Add(scope);
                    }

                    break;

                case ScopeType.Cidr:
                    if (scope.ResolveCidrTarget() is { } cidr &&
                        ScopeTargetNormalizer.CidrContains(cidr.Network, cidr.Prefix, address))
                    {
                        matching.Add(scope);
                    }

                    break;
            }
        }

        var usable = matching.FirstOrDefault(s => s.IsUsable(now));
        if (usable is null)
        {
            var (reason, detail) = Diagnose(matching, now, Swift.Address, address.ToString());
            return (false, reason, detail, null, []);
        }

        if (NetworkAddressClassifier.IsPrivateOrReserved(address))
        {
            if (!_options.AllowPrivateNetworks)
            {
                return (
                    false,
                    ScopeDenialReason.PrivateNetworkBlocked,
                    "Private, loopback, link-local or reserved addresses are disabled by deployment policy.",
                    usable,
                    []);
            }

            if (!usable.HasRestriction(ScopeRestrictions.PrivateNetwork))
            {
                return (
                    false,
                    ScopeDenialReason.PrivateNetworkBlocked,
                    "The matching scope does not carry the 'private-network' restriction required for a private address.",
                    usable,
                    []);
            }
        }

        return (true, ScopeDenialReason.None, $"Address {address} is approved by scope {usable.Id}.", usable, pin);
    }

    private static ScopeEvaluation InactiveOrMissing(
        List<AuthorizedScope> matches,
        DateTimeOffset now,
        Swift kind,
        string target)
    {
        var (reason, detail) = Diagnose(matches, now, kind, target);
        return ScopeEvaluation.Deny(reason, detail, matches.Count > 0 ? matches[0].Id : null);
    }

    private static (ScopeDenialReason Reason, string Detail) Diagnose(
        List<AuthorizedScope> matches,
        DateTimeOffset now,
        Swift kind,
        string target)
    {
        if (matches.Count == 0)
        {
            return (ScopeDenialReason.NoApprovedScope, $"No approved {Describe(kind)} scope matches '{target}'.");
        }

        if (matches.Any(s => s.Status == ScopeApprovalStatus.Revoked))
        {
            return (ScopeDenialReason.ScopeRevoked, $"The {Describe(kind)} scope for '{target}' has been revoked.");
        }

        if (matches.Any(s => s.Status == ScopeApprovalStatus.Expired || s.IsExpired(now)))
        {
            return (ScopeDenialReason.ScopeExpired, $"The {Describe(kind)} scope for '{target}' has expired.");
        }

        if (matches.Any(s => s.Status == ScopeApprovalStatus.Pending))
        {
            return (ScopeDenialReason.ScopePending, $"The {Describe(kind)} scope for '{target}' is pending approval.");
        }

        return (
            ScopeDenialReason.NoApprovedScope,
            $"The {Describe(kind)} scope for '{target}' is approved but not yet valid.");
    }

    private static string Describe(Swift kind) => kind switch
    {
        Swift.Origin => "origin",
        Swift.Hostname => "hostname",
        _ => "address",
    };

    private async Task<bool> IsAuthorizedForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId || workspaceId == Guid.Empty)
        {
            return false;
        }

        var membership = await _dbContext.WorkspaceMemberships
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, cancellationToken);

        return membership is not null &&
               WorkspaceRoles.HasPermission(membership.Role, WorkspacePermissions.ViewScopes);
    }

    private enum Swift
    {
        Address,
        Hostname,
        Origin,
    }
}
