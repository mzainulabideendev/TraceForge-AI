namespace TraceForge.Domain.Audit;

/// <summary>
/// Append-only record of security-sensitive actions. There is intentionally no update or delete
/// path in the application; the table is written by the audit writer only.
/// </summary>
public sealed class AuditLogEntry
{
    public const int ActionMaxLength = 100;
    public const int ActorIdentifierMaxLength = 320;
    public const int TargetTypeMaxLength = 100;
    public const int TargetIdMaxLength = 100;
    public const int IpAddressMaxLength = 64;
    public const int UserAgentMaxLength = 512;
    public const int DetailsMaxLength = 2000;

    private AuditLogEntry()
    {
    }

    private AuditLogEntry(
        Guid? actorUserId,
        string? actorIdentifier,
        string action,
        string targetType,
        string? targetId,
        AuditOutcome outcome,
        string? ipAddress,
        string? userAgent,
        string? details,
        DateTimeOffset occurredAtUtc)
    {
        ActorUserId = actorUserId;
        ActorIdentifier = actorIdentifier;
        Action = action;
        TargetType = targetType;
        TargetId = targetId;
        Outcome = outcome;
        IpAddress = ipAddress;
        UserAgent = userAgent;
        Details = details;
        OccurredAtUtc = occurredAtUtc;
    }

    public long Id { get; private set; }

    public Guid? ActorUserId { get; private set; }

    public string? ActorIdentifier { get; private set; }

    public string Action { get; private set; } = string.Empty;

    public string TargetType { get; private set; } = string.Empty;

    public string? TargetId { get; private set; }

    public AuditOutcome Outcome { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    /// <summary>Short, already-redacted context. Must never contain secrets or credentials.</summary>
    public string? Details { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public static AuditLogEntry Create(
        string action,
        string targetType,
        AuditOutcome outcome,
        DateTimeOffset occurredAtUtc,
        Guid? actorUserId = null,
        string? actorIdentifier = null,
        string? targetId = null,
        string? ipAddress = null,
        string? userAgent = null,
        string? details = null)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            throw new ArgumentException("Audit action is required.", nameof(action));
        }

        if (string.IsNullOrWhiteSpace(targetType))
        {
            throw new ArgumentException("Audit target type is required.", nameof(targetType));
        }

        if (occurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Audit timestamps must be UTC.", nameof(occurredAtUtc));
        }

        return new AuditLogEntry(
            actorUserId,
            Trim(actorIdentifier, ActorIdentifierMaxLength),
            Trim(action, ActionMaxLength)!,
            Trim(targetType, TargetTypeMaxLength)!,
            Trim(targetId, TargetIdMaxLength),
            outcome,
            Trim(ipAddress, IpAddressMaxLength),
            Trim(userAgent, UserAgentMaxLength),
            Trim(details, DetailsMaxLength),
            occurredAtUtc);
    }

    private static string? Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
