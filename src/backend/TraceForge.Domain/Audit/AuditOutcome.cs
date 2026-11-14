namespace TraceForge.Domain.Audit;

/// <summary>
/// Outcome of an audited action. Kept deliberately small: audit records must be unambiguous.
/// </summary>
public enum AuditOutcome
{
    Success = 0,
    Failure = 1,
    Denied = 2,
}
