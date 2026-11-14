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

    public const string WorkspaceCreated = "workspace.created";
    public const string WorkspaceUpdated = "workspace.updated";
    public const string WorkspaceArchived = "workspace.archived";
    public const string WorkspaceMemberAdded = "workspace.member.added";
    public const string WorkspaceMemberRoleChanged = "workspace.member.role.changed";
    public const string WorkspaceMemberRemoved = "workspace.member.removed";
    public const string WorkspaceAccessDenied = "workspace.access.denied";

    public const string ScopeRegistered = "scope.registered";
    public const string ScopeApproved = "scope.approved";
    public const string ScopeRevoked = "scope.revoked";
    public const string ScopeExpired = "scope.expired";
    public const string ScopeRestrictionsUpdated = "scope.restrictions.updated";
}
