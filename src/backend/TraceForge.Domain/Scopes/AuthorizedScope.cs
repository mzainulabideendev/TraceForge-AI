using System.Net;
using System.Net.Sockets;

namespace TraceForge.Domain.Scopes;

/// <summary>
/// An explicitly approved target inside one workspace. Registration creates a <see cref="ScopeApprovalStatus.Pending"/>
/// record; only an explicit approval by an Owner or Administrator makes it usable, and only while it
/// is unexpired and unrevoked.
/// </summary>
public sealed class AuthorizedScope
{
    public const int ApprovalReferenceMinLength = 3;
    public const int ApprovalReferenceMaxLength = 500;

    private AuthorizedScope()
    {
    }

    private AuthorizedScope(
        Guid id,
        Guid workspaceId,
        ScopeType type,
        string normalizedTarget,
        string restrictions,
        DateTimeOffset validFromUtc,
        DateTimeOffset expiresAtUtc,
        Guid createdByUserId,
        DateTimeOffset now)
    {
        Id = id;
        WorkspaceId = workspaceId;
        Type = type;
        NormalizedTarget = normalizedTarget;
        Restrictions = restrictions;
        Status = ScopeApprovalStatus.Pending;
        ValidFromUtc = validFromUtc;
        ExpiresAtUtc = expiresAtUtc;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    public Guid Id { get; private set; }

    public Guid WorkspaceId { get; private set; }

    public ScopeType Type { get; private set; }

    public string NormalizedTarget { get; private set; } = string.Empty;

    public ScopeApprovalStatus Status { get; private set; }

    public string? ApprovalReference { get; private set; }

    public Guid? ApprovedByUserId { get; private set; }

    public DateTimeOffset? ApprovedAtUtc { get; private set; }

    public Guid? RevokedByUserId { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public DateTimeOffset ValidFromUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    /// <summary>Canonical, comma-separated restriction tokens. Empty when unrestricted.</summary>
    public string Restrictions { get; private set; } = string.Empty;

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static AuthorizedScope Create(
        Guid workspaceId,
        ScopeType type,
        string normalizedTarget,
        string? restrictions,
        DateTimeOffset validFromUtc,
        DateTimeOffset expiresAtUtc,
        Guid createdByUserId,
        DateTimeOffset now)
    {
        if (workspaceId == Guid.Empty)
        {
            throw new ArgumentException("A workspace is required.", nameof(workspaceId));
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException("A creator is required.", nameof(createdByUserId));
        }

        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown scope type.");
        }

        if (string.IsNullOrWhiteSpace(normalizedTarget))
        {
            throw new ArgumentException("A normalized target is required.", nameof(normalizedTarget));
        }

        if (!ScopeRestrictions.TryCanonicalize(ScopeRestrictions.Parse(restrictions), out var canonical, out var restrictionError))
        {
            throw new ArgumentException(restrictionError, nameof(restrictions));
        }

        if (now.Offset != TimeSpan.Zero || validFromUtc.Offset != TimeSpan.Zero || expiresAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamps must be UTC.");
        }

        if (expiresAtUtc <= validFromUtc)
        {
            throw new ArgumentException("The expiration time must be after the valid-from time.", nameof(expiresAtUtc));
        }

        if (expiresAtUtc <= now)
        {
            throw new ArgumentException("The expiration time must be in the future.", nameof(expiresAtUtc));
        }

        if (validFromUtc < now.AddMinutes(-1))
        {
            throw new ArgumentException("The valid-from time must not be in the past.", nameof(validFromUtc));
        }

        return new AuthorizedScope(
            Guid.NewGuid(),
            workspaceId,
            type,
            normalizedTarget,
            canonical,
            validFromUtc,
            expiresAtUtc,
            createdByUserId,
            now);
    }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAtUtc;

    public bool IsUsable(DateTimeOffset now) =>
        Status == ScopeApprovalStatus.Approved &&
        !IsExpired(now) &&
        now >= ValidFromUtc;

    public bool HasRestriction(string restriction) =>
        ScopeRestrictions.Has(Restrictions, restriction);

    public void Approve(Guid approverUserId, string approvalReference, DateTimeOffset now)
    {
        if (approverUserId == Guid.Empty)
        {
            throw new ArgumentException("An approver is required.", nameof(approverUserId));
        }

        if (Status == ScopeApprovalStatus.Revoked)
        {
            throw new InvalidOperationException("A revoked scope cannot be approved again; register a new scope.");
        }

        if (IsExpired(now))
        {
            throw new InvalidOperationException("An expired scope cannot be approved.");
        }

        var reference = (approvalReference ?? string.Empty).Trim();
        if (reference.Length is < ApprovalReferenceMinLength or > ApprovalReferenceMaxLength)
        {
            throw new ArgumentException(
                $"Approval evidence must be between {ApprovalReferenceMinLength} and {ApprovalReferenceMaxLength} characters.",
                nameof(approvalReference));
        }

        Status = ScopeApprovalStatus.Approved;
        ApprovalReference = reference;
        ApprovedByUserId = approverUserId;
        ApprovedAtUtc = now;
        UpdatedAtUtc = now;
    }

    public void Revoke(Guid revokedByUserId, DateTimeOffset now)
    {
        if (revokedByUserId == Guid.Empty)
        {
            throw new ArgumentException("A revoking user is required.", nameof(revokedByUserId));
        }

        if (Status == ScopeApprovalStatus.Revoked)
        {
            return;
        }

        Status = ScopeApprovalStatus.Revoked;
        RevokedByUserId = revokedByUserId;
        RevokedAtUtc = now;
        UpdatedAtUtc = now;
    }

    public void Expire(DateTimeOffset now)
    {
        if (!IsExpired(now))
        {
            throw new InvalidOperationException("A scope cannot be expired before its expiration time.");
        }

        if (Status == ScopeApprovalStatus.Expired)
        {
            return;
        }

        Status = ScopeApprovalStatus.Expired;
        UpdatedAtUtc = now;
    }

    /// <summary>
    /// Restriction changes reset approval: an approved scope cannot silently widen or alter its
    /// constraints without a fresh, explicit approval decision.
    /// </summary>
    public void UpdateRestrictions(IReadOnlyCollection<string> restrictions, DateTimeOffset now)
    {
        if (!ScopeRestrictions.TryCanonicalize(restrictions, out var canonical, out var error))
        {
            throw new ArgumentException(error, nameof(restrictions));
        }

        if (string.Equals(canonical, Restrictions, StringComparison.Ordinal))
        {
            return;
        }

        Restrictions = canonical;
        UpdatedAtUtc = now;

        if (Status is ScopeApprovalStatus.Approved or ScopeApprovalStatus.Pending)
        {
            Status = ScopeApprovalStatus.Pending;
            ApprovalReference = null;
            ApprovedByUserId = null;
            ApprovedAtUtc = null;
        }
    }

    /// <summary>Extracts the parsed address for IP scopes; returns null for non-IP scope types.</summary>
    public IPAddress? ResolveAddressTarget() => Type switch
    {
        ScopeType.IPv4 or ScopeType.IPv6 => IPAddress.Parse(NormalizedTarget),
        _ => null,
    };

    public (IPAddress Network, int Prefix)? ResolveCidrTarget()
    {
        if (Type != ScopeType.Cidr || !ScopeTargetNormalizer.TryParseCidr(NormalizedTarget, out var network, out var prefix))
        {
            return null;
        }

        return (network, prefix);
    }

    public OriginTarget? ResolveOriginTarget()
    {
        if (Type != ScopeType.Origin || !ScopeTargetNormalizer.TryParseOrigin(NormalizedTarget, out var origin, out _))
        {
            return null;
        }

        return origin;
    }

    public bool TargetIsPrivateOrReserved()
    {
        switch (Type)
        {
            case ScopeType.IPv4:
            case ScopeType.IPv6:
                return NetworkAddressClassifier.IsPrivateOrReserved(IPAddress.Parse(NormalizedTarget));

            case ScopeType.Cidr:
                var cidr = ResolveCidrTarget();
                return cidr is not null && NetworkAddressClassifier.IsPrivateOrReserved(cidr.Value.Network);

            case ScopeType.Origin:
                var origin = ResolveOriginTarget();
                return origin is { IsIpLiteral: true } &&
                       NetworkAddressClassifier.IsPrivateOrReserved(IPAddress.Parse(origin.Value.Host));

            default:
                return false;
        }
    }

    public static AddressFamily FamilyOf(ScopeType type) =>
        type == ScopeType.IPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
}
