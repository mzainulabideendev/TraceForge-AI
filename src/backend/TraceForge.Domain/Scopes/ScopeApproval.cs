namespace TraceForge.Domain.Scopes;

/// <summary>
/// Durable record of an approval decision for a scope. Kept separate from the scope row so the
/// evidence behind every approval remains queryable even after revocation or expiry.
/// </summary>
public sealed class ScopeApproval
{
    public const int ReferenceMaxLength = AuthorizedScope.ApprovalReferenceMaxLength;

    private ScopeApproval()
    {
    }

    private ScopeApproval(
        long id,
        Guid workspaceId,
        Guid scopeId,
        Guid approvedByUserId,
        string reference,
        DateTimeOffset approvedAtUtc)
    {
        Id = id;
        WorkspaceId = workspaceId;
        ScopeId = scopeId;
        ApprovedByUserId = approvedByUserId;
        Reference = reference;
        ApprovedAtUtc = approvedAtUtc;
    }

    public long Id { get; private set; }

    public Guid WorkspaceId { get; private set; }

    public Guid ScopeId { get; private set; }

    public Guid ApprovedByUserId { get; private set; }

    public string Reference { get; private set; } = string.Empty;

    public DateTimeOffset ApprovedAtUtc { get; private set; }

    public static ScopeApproval Create(
        Guid workspaceId,
        Guid scopeId,
        Guid approvedByUserId,
        string reference,
        DateTimeOffset approvedAtUtc)
    {
        if (workspaceId == Guid.Empty || scopeId == Guid.Empty || approvedByUserId == Guid.Empty)
        {
            throw new ArgumentException("Workspace, scope and approver are required.");
        }

        var trimmed = (reference ?? string.Empty).Trim();
        if (trimmed.Length is < AuthorizedScope.ApprovalReferenceMinLength or > ReferenceMaxLength)
        {
            throw new ArgumentException("Approval evidence is required.", nameof(reference));
        }

        if (approvedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamps must be UTC.", nameof(approvedAtUtc));
        }

        return new ScopeApproval(0, workspaceId, scopeId, approvedByUserId, trimmed, approvedAtUtc);
    }
}
