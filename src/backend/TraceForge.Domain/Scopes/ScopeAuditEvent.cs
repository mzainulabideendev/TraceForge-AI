namespace TraceForge.Domain.Scopes;

/// <summary>
/// Append-only lifecycle history for a scope (registered, approved, revoked, expired, restrictions
/// updated). This complements the global audit log and keeps scope history queryable per scope.
/// There is intentionally no update or delete path in the application.
/// </summary>
public sealed class ScopeAuditEvent
{
    public const int ActionMaxLength = 100;
    public const int DetailsMaxLength = 2000;

    private ScopeAuditEvent()
    {
    }

    private ScopeAuditEvent(
        Guid workspaceId,
        Guid scopeId,
        string action,
        Guid actorUserId,
        string? details,
        DateTimeOffset occurredAtUtc)
    {
        WorkspaceId = workspaceId;
        ScopeId = scopeId;
        Action = action;
        ActorUserId = actorUserId;
        Details = details;
        OccurredAtUtc = occurredAtUtc;
    }

    public long Id { get; private set; }

    public Guid WorkspaceId { get; private set; }

    public Guid ScopeId { get; private set; }

    public string Action { get; private set; } = string.Empty;

    public Guid ActorUserId { get; private set; }

    /// <summary>Short, already-redacted context. Must never contain secrets.</summary>
    public string? Details { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public static ScopeAuditEvent Create(
        Guid workspaceId,
        Guid scopeId,
        string action,
        Guid actorUserId,
        DateTimeOffset occurredAtUtc,
        string? details = null)
    {
        if (workspaceId == Guid.Empty || scopeId == Guid.Empty || actorUserId == Guid.Empty)
        {
            throw new ArgumentException("Workspace, scope and actor are required.");
        }

        if (string.IsNullOrWhiteSpace(action))
        {
            throw new ArgumentException("An action is required.", nameof(action));
        }

        if (occurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamps must be UTC.", nameof(occurredAtUtc));
        }

        var trimmedDetails = string.IsNullOrWhiteSpace(details) ? null : details.Trim();
        if (trimmedDetails is { Length: > DetailsMaxLength })
        {
            trimmedDetails = trimmedDetails[..DetailsMaxLength];
        }

        return new ScopeAuditEvent(
            workspaceId,
            scopeId,
            action.Length > ActionMaxLength ? action[..ActionMaxLength] : action,
            actorUserId,
            trimmedDetails,
            occurredAtUtc);
    }
}
