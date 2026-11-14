namespace TraceForge.Domain.Audit;

/// <summary>
/// Stable audit action names. These are persisted strings, so values must never be renamed
/// without a data migration.
/// </summary>
public static class AuditActions
{
    public const string UserRegistered = "auth.user.registered";
    public const string UserLoginSucceeded = "auth.user.login.succeeded";
    public const string UserLoginFailed = "auth.user.login.failed";
    public const string UserLogout = "auth.user.logout";
    public const string RoleAssigned = "auth.role.assigned";
    public const string UsersListed = "admin.users.listed";
    public const string AuditLogQueried = "admin.audit.queried";
}
