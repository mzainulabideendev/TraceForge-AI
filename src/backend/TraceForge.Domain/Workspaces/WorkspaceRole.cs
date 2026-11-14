namespace TraceForge.Domain.Workspaces;

/// <summary>
/// Role a user holds inside a single workspace. This is deliberately separate from the global
/// Identity role: a global Administrator is not automatically a workspace member.
/// </summary>
public enum WorkspaceRole
{
    Owner = 1,
    Administrator = 2,
    Analyst = 3,
}

/// <summary>
/// Fine-grained workspace operations. Permissions are always evaluated server-side from the stored
/// membership role, never from client input.
/// </summary>
[Flags]
public enum WorkspacePermissions
{
    None = 0,
    ViewWorkspace = 1 << 0,
    UpdateWorkspace = 1 << 1,
    ArchiveWorkspace = 1 << 2,
    ViewMembers = 1 << 3,
    ManageMembers = 1 << 4,
    AssignOwner = 1 << 5,
    ViewScopes = 1 << 6,
    RegisterScopes = 1 << 7,
    ApproveScopes = 1 << 8,
    RevokeScopes = 1 << 9,
    UpdateScopeRestrictions = 1 << 10,
}

/// <summary>
/// Single source of truth for what each workspace role may do.
///
/// <para><b>Owner</b>: everything, including archiving the workspace, assigning the Owner role and
/// changing/removing other owners. The last owner cannot be removed or demoted.</para>
/// <para><b>Administrator</b>: update workspace details, manage non-owner members, register,
/// approve, revoke and restrict scopes. Cannot archive, cannot grant Owner, cannot modify an owner.</para>
/// <para><b>Analyst</b>: read the workspace and its members/scopes, and register scopes that remain
/// pending until an Owner or Administrator approves them. No management rights.</para>
/// </summary>
public static class WorkspaceRoles
{
    private const WorkspacePermissions OwnerPermissions =
        WorkspacePermissions.ViewWorkspace |
        WorkspacePermissions.UpdateWorkspace |
        WorkspacePermissions.ArchiveWorkspace |
        WorkspacePermissions.ViewMembers |
        WorkspacePermissions.ManageMembers |
        WorkspacePermissions.AssignOwner |
        WorkspacePermissions.ViewScopes |
        WorkspacePermissions.RegisterScopes |
        WorkspacePermissions.ApproveScopes |
        WorkspacePermissions.RevokeScopes |
        WorkspacePermissions.UpdateScopeRestrictions;

    private const WorkspacePermissions AdministratorPermissions =
        WorkspacePermissions.ViewWorkspace |
        WorkspacePermissions.UpdateWorkspace |
        WorkspacePermissions.ViewMembers |
        WorkspacePermissions.ManageMembers |
        WorkspacePermissions.ViewScopes |
        WorkspacePermissions.RegisterScopes |
        WorkspacePermissions.ApproveScopes |
        WorkspacePermissions.RevokeScopes |
        WorkspacePermissions.UpdateScopeRestrictions;

    private const WorkspacePermissions AnalystPermissions =
        WorkspacePermissions.ViewWorkspace |
        WorkspacePermissions.ViewMembers |
        WorkspacePermissions.ViewScopes |
        WorkspacePermissions.RegisterScopes;

    public static WorkspacePermissions PermissionsFor(WorkspaceRole role) => role switch
    {
        WorkspaceRole.Owner => OwnerPermissions,
        WorkspaceRole.Administrator => AdministratorPermissions,
        WorkspaceRole.Analyst => AnalystPermissions,
        _ => WorkspacePermissions.None,
    };

    public static bool HasPermission(WorkspaceRole role, WorkspacePermissions permission) =>
        (PermissionsFor(role) & permission) == permission;

    public static bool TryParse(string? value, out WorkspaceRole role) =>
        Enum.TryParse(value, ignoreCase: true, out role) && Enum.IsDefined(role);
}
