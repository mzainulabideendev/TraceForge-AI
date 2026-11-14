using TraceForge.Domain.Audit;

namespace TraceForge.Application.Audit;

/// <summary>Append-only writer for security audit records.</summary>
public interface IAuditLogWriter
{
    Task WriteAsync(AuditLogEntry entry, CancellationToken cancellationToken);
}

/// <summary>Read model of an audit record, safe to expose to administrators.</summary>
public sealed record AuditLogRecord(
    long Id,
    Guid? ActorUserId,
    string? ActorIdentifier,
    string Action,
    string TargetType,
    string? TargetId,
    string Outcome,
    string? IpAddress,
    string? UserAgent,
    string? Details,
    DateTimeOffset OccurredAtUtc);

/// <summary>Paged audit log queries, newest first.</summary>
public interface IAuditLogReader
{
    Task<Common.PagedResult<AuditLogRecord>> ReadAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}
