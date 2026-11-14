using TraceForge.Application.Common;
using TraceForge.Domain.Workspaces;

namespace TraceForge.Application.Workspaces;

public sealed record CreateWorkspaceCommand(string Name, string? Description);

public sealed record UpdateWorkspaceCommand(string Name, string? Description);

public sealed record AddWorkspaceMemberCommand(string Email, WorkspaceRole Role);

public sealed record ChangeWorkspaceMemberRoleCommand(WorkspaceRole Role);

public sealed record WorkspaceSummary(
    Guid Id,
    string Name,
    string? Description,
    string Status,
    WorkspaceRole Role,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record WorkspaceDetail(
    Guid Id,
    string Name,
    string? Description,
    string Status,
    WorkspaceRole CallerRole,
    Guid CreatedByUserId,
    int MemberCount,
    int ScopeCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ArchivedAtUtc);

public sealed record WorkspaceMemberRecord(
    Guid UserId,
    string Email,
    string DisplayName,
    WorkspaceRole Role,
    DateTimeOffset AddedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Workspace use cases. Every method resolves the caller's membership from the database and
/// enforces role permissions; no caller-supplied workspace role or membership is trusted.
/// </summary>
public interface IWorkspaceService
{
    Task<OperationResult<WorkspaceDetail>> CreateAsync(
        CreateWorkspaceCommand command,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkspaceSummary>> ListAccessibleAsync(CancellationToken cancellationToken);

    Task<OperationResult<WorkspaceDetail>> GetAsync(Guid workspaceId, CancellationToken cancellationToken);

    Task<OperationResult<WorkspaceDetail>> UpdateAsync(
        Guid workspaceId,
        UpdateWorkspaceCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<WorkspaceDetail>> ArchiveAsync(Guid workspaceId, CancellationToken cancellationToken);

    Task<OperationResult<IReadOnlyList<WorkspaceMemberRecord>>> ListMembersAsync(
        Guid workspaceId,
        CancellationToken cancellationToken);

    Task<OperationResult<WorkspaceMemberRecord>> AddMemberAsync(
        Guid workspaceId,
        AddWorkspaceMemberCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<WorkspaceMemberRecord>> ChangeMemberRoleAsync(
        Guid workspaceId,
        Guid targetUserId,
        ChangeWorkspaceMemberRoleCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<bool>> RemoveMemberAsync(
        Guid workspaceId,
        Guid targetUserId,
        CancellationToken cancellationToken);
}
