using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TraceForge.Application.Audit;
using TraceForge.Application.Common;
using TraceForge.Application.Workspaces;
using TraceForge.Domain.Audit;
using TraceForge.Domain.Workspaces;
using TraceForge.Infrastructure.Persistence;

namespace TraceForge.Infrastructure.Workspaces;

/// <summary>
/// Workspace and membership use cases. Access is always resolved from the caller's stored
/// membership; a workspace id in a URL grants nothing on its own.
/// </summary>
internal sealed class WorkspaceService : IWorkspaceService
{
    private readonly TraceForgeDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogWriter _auditLog;
    private readonly IRequestContext _requestContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WorkspaceService> _logger;

    public WorkspaceService(
        TraceForgeDbContext dbContext,
        ICurrentUser currentUser,
        IAuditLogWriter auditLog,
        IRequestContext requestContext,
        TimeProvider timeProvider,
        ILogger<WorkspaceService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _auditLog = auditLog;
        _requestContext = requestContext;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<OperationResult<WorkspaceDetail>> CreateAsync(
        CreateWorkspaceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (_currentUser.UserId is not { } userId)
        {
            return OperationResults.Fail<WorkspaceDetail>(OperationFailure.Forbidden, "Authentication is required.");
        }

        Workspace workspace;
        try
        {
            workspace = Workspace.Create(
                command.Name,
                command.Description,
                userId,
                _timeProvider.GetUtcNow());
        }
        catch (ArgumentException exception)
        {
            return OperationResults.Fail<WorkspaceDetail>(OperationFailure.Validation, exception.Message);
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var membership = WorkspaceMembership.Create(
                workspace.Id,
                userId,
                WorkspaceRole.Owner,
                _timeProvider.GetUtcNow());

            _dbContext.Workspaces.Add(workspace);
            _dbContext.WorkspaceMemberships.Add(membership);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await AuditAsync(
                AuditActions.WorkspaceCreated,
                AuditOutcome.Success,
                workspace.Id.ToString(),
                workspace.Id.ToString(),
                $"name={workspace.Name}",
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return OperationResults.Success(await BuildDetailAsync(workspace, WorkspaceRole.Owner, cancellationToken));
    }

    public async Task<IReadOnlyList<WorkspaceSummary>> ListAccessibleAsync(CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return [];
        }

        var rows = await _dbContext.WorkspaceMemberships
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .Join(
                _dbContext.Workspaces.AsNoTracking(),
                membership => membership.WorkspaceId,
                workspace => workspace.Id,
                (membership, workspace) => new { membership.Role, workspace })
            .OrderBy(x => x.workspace.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new WorkspaceSummary(
                x.workspace.Id,
                x.workspace.Name,
                x.workspace.Description,
                x.workspace.Status.ToString(),
                x.Role,
                x.workspace.CreatedAtUtc,
                x.workspace.UpdatedAtUtc))
            .ToList();
    }

    public async Task<OperationResult<WorkspaceDetail>> GetAsync(
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(workspaceId, cancellationToken);
        if (access is null)
        {
            return await NotFoundAsync<WorkspaceDetail>(workspaceId, cancellationToken);
        }

        if (!WorkspaceRoles.HasPermission(access.Role, WorkspacePermissions.ViewWorkspace))
        {
            return await ForbiddenAsync<WorkspaceDetail>(workspaceId, cancellationToken);
        }

        return OperationResults.Success(await BuildDetailAsync(access.Workspace, access.Role, cancellationToken));
    }

    public async Task<OperationResult<WorkspaceDetail>> UpdateAsync(
        Guid workspaceId,
        UpdateWorkspaceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var access = await ResolveAccessAsync(workspaceId, cancellationToken);
        if (access is null)
        {
            return await NotFoundAsync<WorkspaceDetail>(workspaceId, cancellationToken);
        }

        if (!WorkspaceRoles.HasPermission(access.Role, WorkspacePermissions.UpdateWorkspace))
        {
            return await ForbiddenAsync<WorkspaceDetail>(workspaceId, cancellationToken);
        }

        if (access.Workspace.IsArchived)
        {
            return OperationResults.Fail<WorkspaceDetail>(OperationFailure.Archived, "An archived workspace cannot be modified.");
        }

        try
        {
            access.Workspace.Update(command.Name, command.Description, _timeProvider.GetUtcNow());
        }
        catch (ArgumentException exception)
        {
            return OperationResults.Fail<WorkspaceDetail>(OperationFailure.Validation, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return OperationResults.Fail<WorkspaceDetail>(OperationFailure.Archived, exception.Message);
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await AuditAsync(
                AuditActions.WorkspaceUpdated,
                AuditOutcome.Success,
                workspaceId.ToString(),
                workspaceId.ToString(),
                $"name={access.Workspace.Name}",
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return OperationResults.Success(await BuildDetailAsync(access.Workspace, access.Role, cancellationToken));
    }

    public async Task<OperationResult<WorkspaceDetail>> ArchiveAsync(
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(workspaceId, cancellationToken);
        if (access is null)
        {
            return await NotFoundAsync<WorkspaceDetail>(workspaceId, cancellationToken);
        }

        if (!WorkspaceRoles.HasPermission(access.Role, WorkspacePermissions.ArchiveWorkspace))
        {
            return await ForbiddenAsync<WorkspaceDetail>(workspaceId, cancellationToken);
        }

        if (access.Workspace.IsArchived)
        {
            return OperationResults.Success(await BuildDetailAsync(access.Workspace, access.Role, cancellationToken));
        }

        access.Workspace.Archive(_timeProvider.GetUtcNow());

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await AuditAsync(
                AuditActions.WorkspaceArchived,
                AuditOutcome.Success,
                workspaceId.ToString(),
                workspaceId.ToString(),
                null,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return OperationResults.Success(await BuildDetailAsync(access.Workspace, access.Role, cancellationToken));
    }

    public async Task<OperationResult<IReadOnlyList<WorkspaceMemberRecord>>> ListMembersAsync(
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(workspaceId, cancellationToken);
        if (access is null)
        {
            return await NotFoundAsync<IReadOnlyList<WorkspaceMemberRecord>>(workspaceId, cancellationToken);
        }

        if (!WorkspaceRoles.HasPermission(access.Role, WorkspacePermissions.ViewMembers))
        {
            return await ForbiddenAsync<IReadOnlyList<WorkspaceMemberRecord>>(workspaceId, cancellationToken);
        }

        var members = await ReadMembersAsync(workspaceId, cancellationToken);
        return OperationResults.Success<IReadOnlyList<WorkspaceMemberRecord>>(members);
    }

    public async Task<OperationResult<WorkspaceMemberRecord>> AddMemberAsync(
        Guid workspaceId,
        AddWorkspaceMemberCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var access = await ResolveAccessAsync(workspaceId, cancellationToken);
        if (access is null)
        {
            return await NotFoundAsync<WorkspaceMemberRecord>(workspaceId, cancellationToken);
        }

        if (!WorkspaceRoles.HasPermission(access.Role, WorkspacePermissions.ManageMembers))
        {
            return await ForbiddenAsync<WorkspaceMemberRecord>(workspaceId, cancellationToken);
        }

        if (access.Workspace.IsArchived)
        {
            return OperationResults.Fail<WorkspaceMemberRecord>(OperationFailure.Archived, "An archived workspace cannot be modified.");
        }

        if (!Enum.IsDefined(command.Role))
        {
            return OperationResults.Fail<WorkspaceMemberRecord>(OperationFailure.Validation, "Unknown workspace role.");
        }

        if (command.Role == WorkspaceRole.Owner &&
            !WorkspaceRoles.HasPermission(access.Role, WorkspacePermissions.AssignOwner))
        {
            return OperationResults.Fail<WorkspaceMemberRecord>(
                OperationFailure.Forbidden,
                "Only an owner can grant the owner role.");
        }

        if (string.IsNullOrWhiteSpace(command.Email))
        {
            return OperationResults.Fail<WorkspaceMemberRecord>(OperationFailure.Validation, "E-mail is required.");
        }

        var email = command.Email.Trim();
        if (email.Length > 320)
        {
            return OperationResults.Fail<WorkspaceMemberRecord>(OperationFailure.Validation, "E-mail is not a valid address.");
        }

        var normalizedEmail = email.ToUpperInvariant();
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

        if (user is null)
        {
            return OperationResults.Fail<WorkspaceMemberRecord>(
                OperationFailure.Validation,
                "No account with that e-mail address exists.");
        }

        var alreadyMember = await _dbContext.WorkspaceMemberships
            .AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == user.Id, cancellationToken);

        if (alreadyMember)
        {
            return OperationResults.Fail<WorkspaceMemberRecord>(
                OperationFailure.Conflict,
                "The user is already a member of this workspace.");
        }

        var membership = WorkspaceMembership.Create(
            workspaceId,
            user.Id,
            command.Role,
            _timeProvider.GetUtcNow());

        _dbContext.WorkspaceMemberships.Add(membership);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await AuditAsync(
                AuditActions.WorkspaceMemberAdded,
                AuditOutcome.Success,
                user.Id.ToString(),
                workspaceId.ToString(),
                $"role={command.Role}",
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return OperationResults.Success(new WorkspaceMemberRecord(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            membership.Role,
            membership.CreatedAtUtc,
            membership.UpdatedAtUtc));
    }

    public async Task<OperationResult<WorkspaceMemberRecord>> ChangeMemberRoleAsync(
        Guid workspaceId,
        Guid targetUserId,
        ChangeWorkspaceMemberRoleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var access = await ResolveAccessAsync(workspaceId, cancellationToken);
        if (access is null)
        {
            return await NotFoundAsync<WorkspaceMemberRecord>(workspaceId, cancellationToken);
        }

        if (!WorkspaceRoles.HasPermission(access.Role, WorkspacePermissions.ManageMembers))
        {
            return await ForbiddenAsync<WorkspaceMemberRecord>(workspaceId, cancellationToken);
        }

        if (access.Workspace.IsArchived)
        {
            return OperationResults.Fail<WorkspaceMemberRecord>(OperationFailure.Archived, "An archived workspace cannot be modified.");
        }

        if (!Enum.IsDefined(command.Role))
        {
            return OperationResults.Fail<WorkspaceMemberRecord>(OperationFailure.Validation, "Unknown workspace role.");
        }

        var target = await _dbContext.WorkspaceMemberships
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == targetUserId, cancellationToken);

        if (target is null)
        {
            return OperationResults.Fail<WorkspaceMemberRecord>(OperationFailure.NotFound, "The member was not found.");
        }

        if (target.Role == WorkspaceRole.Owner || command.Role == WorkspaceRole.Owner)
        {
            if (!WorkspaceRoles.HasPermission(access.Role, WorkspacePermissions.AssignOwner))
            {
                return OperationResults.Fail<WorkspaceMemberRecord>(
                    OperationFailure.Forbidden,
                    "Only an owner can change the owner role.");
            }
        }

        if (target.Role == WorkspaceRole.Owner && command.Role != WorkspaceRole.Owner)
        {
            var ownerCount = await _dbContext.WorkspaceMemberships
                .CountAsync(m => m.WorkspaceId == workspaceId && m.Role == WorkspaceRole.Owner, cancellationToken);

            if (ownerCount <= 1)
            {
                return OperationResults.Fail<WorkspaceMemberRecord>(
                    OperationFailure.LastOwner,
                    "The last owner of a workspace cannot be demoted.");
            }
        }

        target.ChangeRole(command.Role, _timeProvider.GetUtcNow());

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await AuditAsync(
                AuditActions.WorkspaceMemberRoleChanged,
                AuditOutcome.Success,
                targetUserId.ToString(),
                workspaceId.ToString(),
                $"role={command.Role}",
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        var user = await _dbContext.Users.AsNoTracking()
            .FirstAsync(u => u.Id == targetUserId, cancellationToken);

        return OperationResults.Success(new WorkspaceMemberRecord(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            target.Role,
            target.CreatedAtUtc,
            target.UpdatedAtUtc));
    }

    public async Task<OperationResult<bool>> RemoveMemberAsync(
        Guid workspaceId,
        Guid targetUserId,
        CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(workspaceId, cancellationToken);
        if (access is null)
        {
            return await NotFoundAsync<bool>(workspaceId, cancellationToken);
        }

        if (!WorkspaceRoles.HasPermission(access.Role, WorkspacePermissions.ManageMembers))
        {
            return await ForbiddenAsync<bool>(workspaceId, cancellationToken);
        }

        if (access.Workspace.IsArchived)
        {
            return OperationResults.Fail<bool>(OperationFailure.Archived, "An archived workspace cannot be modified.");
        }

        var target = await _dbContext.WorkspaceMemberships
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == targetUserId, cancellationToken);

        if (target is null)
        {
            return OperationResults.Fail<bool>(OperationFailure.NotFound, "The member was not found.");
        }

        if (target.Role == WorkspaceRole.Owner &&
            !WorkspaceRoles.HasPermission(access.Role, WorkspacePermissions.AssignOwner))
        {
            return OperationResults.Fail<bool>(OperationFailure.Forbidden, "Only an owner can remove an owner.");
        }

        if (target.Role == WorkspaceRole.Owner)
        {
            var ownerCount = await _dbContext.WorkspaceMemberships
                .CountAsync(m => m.WorkspaceId == workspaceId && m.Role == WorkspaceRole.Owner, cancellationToken);

            if (ownerCount <= 1)
            {
                return OperationResults.Fail<bool>(
                    OperationFailure.LastOwner,
                    "The last owner of a workspace cannot be removed.");
            }
        }

        _dbContext.WorkspaceMemberships.Remove(target);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await AuditAsync(
                AuditActions.WorkspaceMemberRemoved,
                AuditOutcome.Success,
                targetUserId.ToString(),
                workspaceId.ToString(),
                null,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return OperationResults.Success(true);
    }

    private async Task<WorkspaceAccess?> ResolveAccessAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId || workspaceId == Guid.Empty)
        {
            return null;
        }

        var workspace = await _dbContext.Workspaces
            .FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);

        if (workspace is null)
        {
            return null;
        }

        var membership = await _dbContext.WorkspaceMemberships
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, cancellationToken);

        return membership is null ? null : new WorkspaceAccess(workspace, membership.Role);
    }

    private async Task<IReadOnlyList<WorkspaceMemberRecord>> ReadMembersAsync(
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        var rows = await _dbContext.WorkspaceMemberships
            .AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId)
            .Join(
                _dbContext.Users.AsNoTracking(),
                membership => membership.UserId,
                user => user.Id,
                (membership, user) => new { membership, user })
            .OrderBy(x => x.membership.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => new WorkspaceMemberRecord(
                x.user.Id,
                x.user.Email ?? string.Empty,
                x.user.DisplayName,
                x.membership.Role,
                x.membership.CreatedAtUtc,
                x.membership.UpdatedAtUtc))
            .ToList();
    }

    private async Task<WorkspaceDetail> BuildDetailAsync(
        Workspace workspace,
        WorkspaceRole role,
        CancellationToken cancellationToken)
    {
        var memberCount = await _dbContext.WorkspaceMemberships
            .CountAsync(m => m.WorkspaceId == workspace.Id, cancellationToken);

        var scopeCount = await _dbContext.AuthorizedScopes
            .CountAsync(s => s.WorkspaceId == workspace.Id, cancellationToken);

        return new WorkspaceDetail(
            workspace.Id,
            workspace.Name,
            workspace.Description,
            workspace.Status.ToString(),
            role,
            workspace.CreatedByUserId,
            memberCount,
            scopeCount,
            workspace.CreatedAtUtc,
            workspace.UpdatedAtUtc,
            workspace.ArchivedAtUtc);
    }

    /// <summary>
    /// A non-member and a missing workspace are indistinguishable on purpose: both yield 404 so
    /// callers cannot enumerate workspace ids.
    /// </summary>
    private async Task<OperationResult<T>> NotFoundAsync<T>(Guid workspaceId, CancellationToken cancellationToken)
    {
        await AuditAsync(
            AuditActions.WorkspaceAccessDenied,
            AuditOutcome.Denied,
            workspaceId.ToString(),
            null,
            "workspace-not-found-or-not-member",
            cancellationToken);

        return OperationResults.Fail<T>(OperationFailure.NotFound, "The workspace was not found.");
    }

    private async Task<OperationResult<T>> ForbiddenAsync<T>(Guid workspaceId, CancellationToken cancellationToken)
    {
        await AuditAsync(
            AuditActions.WorkspaceAccessDenied,
            AuditOutcome.Denied,
            workspaceId.ToString(),
            null,
            "insufficient-workspace-role",
            cancellationToken);

        return OperationResults.Fail<T>(
            OperationFailure.Forbidden,
            "The caller's workspace role does not permit this operation.");
    }

    private async Task AuditAsync(
        string action,
        AuditOutcome outcome,
        string? targetId,
        string? workspaceId,
        string? details,
        CancellationToken cancellationToken)
    {
        try
        {
            var entry = AuditLogEntry.Create(
                action,
                targetType: "workspace",
                outcome,
                occurredAtUtc: _timeProvider.GetUtcNow(),
                actorUserId: _currentUser.UserId,
                actorIdentifier: _currentUser.Email,
                targetId: targetId,
                ipAddress: _requestContext.IpAddress,
                userAgent: _requestContext.UserAgent,
                details: workspaceId is null ? details : $"{details};workspace={workspaceId}".TrimStart(';'));

            await _auditLog.WriteAsync(entry, cancellationToken);
        }
        catch (Exception exception)
        {
            // A failed audit write must be visible but must not mask the authorization decision.
            _logger.LogError(exception, "Failed to write workspace audit record for action {Action}.", action);
        }
    }

    private sealed record WorkspaceAccess(Workspace Workspace, WorkspaceRole Role);
}
