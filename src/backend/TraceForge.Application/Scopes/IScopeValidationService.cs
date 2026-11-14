using System.Net;

namespace TraceForge.Application.Scopes;

/// <summary>What the caller wants to reach. A raw target plus optional scheme/port context.</summary>
public sealed record ScopeCheckRequest(string Target, string? Scheme = null, int? Port = null);

public enum ScopeDenialReason
{
    None = 0,
    MalformedTarget = 1,
    NoApprovedScope = 2,
    ScopePending = 3,
    ScopeExpired = 4,
    ScopeRevoked = 5,
    SchemeOrPortNotApproved = 6,
    PrivateNetworkBlocked = 7,
    ResolvedAddressNotApproved = 8,
    DnsResolutionFailed = 9,
    RedirectOutsideScope = 10,
}

/// <summary>
/// Deterministic authorization decision. <see cref="PinnedAddresses"/> are the validated addresses a
/// caller must connect to; the caller must not re-resolve the hostname after this decision.
/// </summary>
public sealed record ScopeEvaluation(
    bool Allowed,
    ScopeDenialReason Reason,
    string Detail,
    Guid? MatchedScopeId,
    IReadOnlyList<IPAddress> PinnedAddresses)
{
    public static ScopeEvaluation Allow(Guid scopeId, IReadOnlyList<IPAddress> pinned, string detail) =>
        new(true, ScopeDenialReason.None, detail, scopeId, pinned);

    public static ScopeEvaluation Deny(ScopeDenialReason reason, string detail, Guid? scopeId = null) =>
        new(false, reason, detail, scopeId, []);
}

/// <summary>
/// Resolves a hostname to addresses. Kept as a port so the deterministic policy can be tested with a
/// controlled resolver and so production DNS behaviour is never hidden inside the policy.
/// </summary>
public interface IDnsResolver
{
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string hostname, CancellationToken cancellationToken);
}

/// <summary>
/// Evaluates a requested destination against the workspace's approved scopes. DNS resolution and
/// address validation are separate steps from the domain-level approval decision.
/// </summary>
public interface IScopeValidationService
{
    Task<ScopeEvaluation> EvaluateAsync(
        Guid workspaceId,
        ScopeCheckRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Validates a redirect target. It must be inside the already-approved origin (scheme, host and
    /// port) and independently allowed by the workspace scopes.
    /// </summary>
    Task<ScopeEvaluation> EvaluateRedirectAsync(
        Guid workspaceId,
        ScopeCheckRequest originalRequest,
        Uri redirect,
        CancellationToken cancellationToken);
}

/// <summary>Deployment-level scope policy, bound from "TraceForge:Scopes".</summary>
public sealed class ScopeValidationOptions
{
    public const string SectionName = "TraceForge:Scopes";

    /// <summary>
    /// Must stay false outside an isolated lab. Even when true, a matching scope must also carry the
    /// "private-network" restriction before a private destination is allowed.
    /// </summary>
    public bool AllowPrivateNetworks { get; init; }
}
