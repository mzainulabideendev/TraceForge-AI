using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TraceForge.Application.Audit;
using TraceForge.Application.Common;
using TraceForge.Application.Scopes;
using TraceForge.Domain.Audit;
using TraceForge.Domain.Scopes;
using TraceForge.Domain.Workspaces;
using TraceForge.Infrastructure.Persistence;

namespace TraceForge.Infrastructure.Scopes;

/// <summary>
/// Authorized-scope management. Registration always leaves a scope pending; approval is a separate,
/// explicit and audited action, and target normalisation happens before persistence.
/// </summary>
internal sealed class ScopeService : IScopeService
{
    private readonly TraceForgeDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogWriter _auditLog;
    private readonly IRequestContext _requestContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ScopeService> _logger;

    public ScopeService(
        TraceForgeDbContext dbContext,
        ICurrentUser currentUser,
        IAuditLogWriter auditLog,
        IRequestContext requestContext,
        TimeProvider timeProvider,
        ILogger<ScopeService> logger)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _auditLog = auditLog;
        _requestContext = requestContext;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<OperationResult<ScopeRecord>> RegisterAsync(
        Guid workspaceId,
        RegisterScopeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var access = await ResolveAccessAsync(workspaceId, cancellationToken);
        if (access is null)
        {
            return await NotFoundAsync<ScopeRecord>(workspaceId, cancellationToken);
        }

        if (!WorkspaceRoles.HasPermission(access.Role, WorkspacePermissions.RegisterScopes))
        {
            return await ForbiddenAsync<ScopeRecord>(workspaceId, cancellationToken);
        }

        if (access.IsArchived)
        {
            return OperationResults.Fail<ScopeRecord>(OperationFailure.Archived, "An archived workspace cannot accept new scopes.");
        }

        if (!Enum.IsDefined(command.Type))
        {
            return OperationResults.Fail<ScopeRecord>(OperationFailure.Validation, "Unknown scope type.");
        }

        if (!ScopeTargetNormalizer.TryNormalize(command.Type, command.Target, out var normalized, out var error))
        {
            return OperationResults.Fail<ScopeRecord>(OperationFailure.Validation, error ?? "The target is invalid.");
        }

        var duplicate = await _dbContext.AuthorizedScopes.AnyAsync(
            s => s.WorkspaceId == workspaceId && s.Type == command.Type && s.NormalizedTarget == normalized,
            cancellationToken);

        if (duplicate)
        {
            return OperationResults.Fail<ScopeRecord>(
                OperationFailure.Conflict,
                "An identical scope already exists in this workspace.");
        }

        AuthorizedScope scope;
        try
        {
            scope = AuthorizedScope.Create(
                workspaceId,
                command.Type,
                normalized,
                string.Join(",", command.Restrictions ?? []),
                command.ValidFromUtc,
                command.ExpiresAtUtc,
                access.UserId,
                _timeProvider.GetUtcNow());
        }
        catch (ArgumentException exception)
        {
            return OperationResults.Fail<ScopeRecord>(OperationFailure.Validation, exception.Message);
        }

        _dbContext.AuthorizedScopes.Add(scope);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await AuditAsync(
                AuditActions.ScopeRegistered,
                AuditOutcome.Success,
                workspaceId,
                scope.Id,
                $"type={scope.Type};target={scope.NormalizedTarget}",
                cancellationToken);
            AddEvent(scope, AuditActions.ScopeRegistered, "pending approval");
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return OperationResults.Success(ToRecord(scope));
    }

    public async Task<OperationResult<IReadOnlyList<ScopeRecord>>> ListAsync(
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(workspaceId, cancellationToken);
        if (access is null)
        {
            return await NotFoundAsync<IReadOnlyList<ScopeRecord>>(workspaceId, cancellationToken);
        }

        if (!WorkspaceRoles.HasPermission(access.Role, WorkspacePermissions.ViewScopes))
        {
            return await ForbiddenAsync<IReadOnlyList<ScopeRecord>>(workspaceId, cancellationToken);
        }

        var scopes = await _dbContext.AuthorizedScopes
            .AsNoTracking()
            .Where(s => s.WorkspaceId == workspaceId)
            .OrderBy(s => s.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return OperationResults.Success<IReadOnlyList<ScopeRecord>>(scopes.Select(ToRecord).ToArray());
    }

    public async Task<OperationResult<ScopeRecord>> GetAsync(
        Guid workspaceId,
        Guid scopeId,
        CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(workspaceId, cancellationToken);
        if (access is null)
        {
            return await NotFoundAsync<ScopeRecord>(workspaceId, cancellationToken);
        }

        if (!WorkspaceRoles.HasPermission(access.Role, WorkspacePermissions.ViewScopes))
        {
            return await ForbiddenAsync<ScopeRecord>(workspaceId, cancellationToken);
        }

        var scope = await _dbContext.AuthorizedScopes
            .FirstOrDefaultAsync(s => s.Id == scopeId && s.WorkspaceId == workspaceId, cancellationToken);

        return scope is null
            ? OperationResults.Fail<ScopeRecord>(OperationFailure.NotFound, "The scope was not found.")
            : OperationResults.Success(ToRecord(scope));
    }

    public async Task<OperationResult<ScopeRecord>> ApproveAsync(
        Guid workspaceId,
        Guid scopeId,
        ApproveScopeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var access = await ResolveAccessAsync(workspaceId, cancellationToken);
        if (access is null)
        {
            return await NotFoundAsync<ScopeRecord>(workspaceId, cancellationToken);
        }

        if (!WorkspaceRoles.HasPermission(access.Role, WorkspacePermissions.ApproveScopes))
        {
            return await ForbiddenAsync<ScopeRecord>(workspaceId, cancellationToken);
        }

        if (access.IsArchived)
        {
            return OperationResults.Fail<ScopeRecord>(OperationFailure.Archived, "An archived workspace cannot approve scopes.");
        }

        var scope = await _dbContext.AuthorizedScopes
            .FirstOrDefaultAsync(s => s.Id == scopeId && s.WorkspaceId == workspaceId, cancellationToken);

        if (scope is null)
        {
            return OperationResults.Fail<ScopeRecord>(OperationFailure.NotFound, "The scope was not found.");
        }

        var now = _timeProvider.GetUtcNow();

        try
        {
            scope.Approve(access.UserId, command.ApprovalReference, now);
        }
        catch (ArgumentException exception)
        {
            return OperationResults.Fail<ScopeRecord>(OperationFailure.Validation, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return OperationResults.Fail<ScopeRecord>(OperationFailure.Conflict, exception.Message);
        }

        _dbContext.ScopeApprovals.Add(ScopeApproval.Create(
            workspaceId,
            scope.Id,
            access.UserId,
            scope.ApprovalReference!,
            now));

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await AuditAsync(
                AuditActions.ScopeApproved,
                AuditOutcome.Success,
                workspaceId,
                scope.Id,
                $"type={scope.Type};target={scope.NormalizedTarget}",
                cancellationToken);
            AddEvent(scope, AuditActions.ScopeApproved, "explicit approval recorded");
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return OperationResults.Success(ToRecord(scope));
    }

    public async Task<OperationResult<ScopeRecord>> RevokeAsync(
        Guid workspaceId,
        Guid scopeId,
        RevokeScopeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var access = await ResolveAccessAsync(workspaceId, cancellationToken);
        if (access is null)
        {
            return await NotFoundAsync<ScopeRecord>(workspaceId, cancellationToken);
        }

        if (!WorkspaceRoles.HasPermission(access.Role, WorkspacePermissions.RevokeScopes))
        {
            return await ForbiddenAsync<ScopeRecord>(workspaceId, cancellationToken);
        }

        var scope = await _dbContext.AuthorizedScopes
            .FirstOrDefaultAsync(s => s.Id == scopeId && s.WorkspaceId == workspaceId, cancellationToken);

        if (scope is null)
        {
            return OperationResults.Fail<ScopeRecord>(OperationFailure.NotFound, "The scope was not found.");
        }

        scope.Revoke(access.UserId, _timeProvider.GetUtcNow());

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await AuditAsync(
                AuditActions.ScopeRevoked,
                AuditOutcome.Success,
                workspaceId,
                scope.Id,
                $"type={scope.Type};target={scope.NormalizedTarget};reason={Sanitize(command.Reason)}",
                cancellationToken);
            AddEvent(scope, AuditActions.ScopeRevoked, Sanitize(command.Reason));
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return OperationResults.Success(ToRecord(scope));
    }

    public async Task<OperationResult<ScopeRecord>> ExpireAsync(
        Guid workspaceId,
        Guid scopeId,
        CancellationToken cancellationToken)
    {
        var access = await ResolveAccessAsync(workspaceId, cancellationToken);
        if (access is null)
        {
            return await NotFoundAsync<ScopeRecord>(workspaceId, cancellationToken);
        }

        if (!WorkspaceRoles.HasPermission(access.Role, WorkspacePermissions.RevokeScopes))
        {
            return await ForbiddenAsync<ScopeRecord>(workspaceId, cancellationToken);
        }

        var scope = await _dbContext.AuthorizedScopes
            .FirstOrDefaultAsync(s => s.Id == scopeId && s.WorkspaceId == workspaceId, cancellationToken);

        if (scope is null)
        {
            return OperationResults.Fail<ScopeRecord>(OperationFailure.NotFound, "The scope was not found.");
        }

        var now = _timeProvider.GetUtcNow();

        try
        {
            scope.Expire(now);
        }
        catch (InvalidOperationException exception)
        {
            return OperationResults.Fail<ScopeRecord>(OperationFailure.Conflict, exception.Message);
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await AuditAsync(
                AuditActions.ScopeExpired,
                AuditOutcome.Success,
                workspaceId,
                scope.Id,
                $"type={scope.Type};target={scope.NormalizedTarget}",
                cancellationToken);
            AddEvent(scope, AuditActions.ScopeExpired, "expiration enforced");
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return OperationResults.Success(ToRecord(scope));
    }

    public async Task<OperationResult<ScopeRecord>> UpdateRestrictionsAsync(
        Guid workspaceId,
        Guid scopeId,
        UpdateScopeRestrictionsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var access = await ResolveAccessAsync(workspaceId, cancellationToken);
        if (access is null)
        {
            return await NotFoundAsync<ScopeRecord>(workspaceId, cancellationToken);
        }

        if (!WorkspaceRoles.HasPermission(access.Role, WorkspacePermissions.UpdateScopeRestrictions))
        {
            return await ForbiddenAsync<ScopeRecord>(workspaceId, cancellationToken);
        }

        if (access.IsArchived)
        {
            return OperationResults.Fail<ScopeRecord>(OperationFailure.Archived, "An archived workspace cannot modify scopes.");
        }

        var scope = await _dbContext.AuthorizedScopes
            .FirstOrDefaultAsync(s => s.Id == scopeId && s.WorkspaceId == workspaceId, cancellationToken);

        if (scope is null)
        {
            return OperationResults.Fail<ScopeRecord>(OperationFailure.NotFound, "The scope was not found.");
        }

        var previousStatus = scope.Status;

        try
        {
            scope.UpdateRestrictions(command.Restrictions ?? [], _timeProvider.GetUtcNow());
        }
        catch (ArgumentException exception)
        {
            return OperationResults.Fail<ScopeRecord>(OperationFailure.Validation, exception.Message);
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await AuditAsync(
                AuditActions.ScopeRestrictionsUpdated,
                AuditOutcome.Success,
                workspaceId,
                scope.Id,
                $"type={scope.Type};target={scope.NormalizedTarget};restrictions={scope.Restrictions};status={previousStatus}->{scope.Status}",
                cancellationToken);
            AddEvent(scope, AuditActions.ScopeRestrictionsUpdated, $"status={previousStatus}->{scope.Status}");
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return OperationResults.Success(ToRecord(scope));
    }

    private ScopeRecord ToRecord(AuthorizedScope scope) => new(
        scope.Id,
        scope.WorkspaceId,
        scope.Type,
        scope.NormalizedTarget,
        scope.Status.ToString(),
        scope.ApprovalReference,
        scope.ApprovedByUserId,
        scope.ApprovedAtUtc,
        scope.RevokedByUserId,
        scope.RevokedAtUtc,
        scope.ValidFromUtc,
        scope.ExpiresAtUtc,
        ScopeRestrictions.Parse(scope.Restrictions),
        scope.CreatedByUserId,
        scope.CreatedAtUtc,
        scope.UpdatedAtUtc,
        scope.IsUsable(_timeProvider.GetUtcNow()));

    private void AddEvent(AuthorizedScope scope, string action, string? details)
    {
        _dbContext.ScopeAuditEvents.Add(ScopeAuditEvent.Create(
            scope.WorkspaceId,
            scope.Id,
            action,
            _currentUser.UserId ?? Guid.Empty,
            _timeProvider.GetUtcNow(),
            details));
    }

    private async Task<ScopeAccess?> ResolveAccessAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not { } userId || workspaceId == Guid.Empty)
        {
            return null;
        }

        var workspace = await _dbContext.Workspaces
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);

        if (workspace is null)
        {
            return null;
        }

        var membership = await _dbContext.WorkspaceMemberships
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, cancellationToken);

        return membership is null
            ? null
            : new ScopeAccess(userId, membership.Role, workspace.IsArchived);
    }

    private async Task<OperationResult<T>> NotFoundAsync<T>(Guid workspaceId, CancellationToken cancellationToken)
    {
        await AuditAsync(
            AuditActions.WorkspaceAccessDenied,
            AuditOutcome.Denied,
            workspaceId,
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
            workspaceId,
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
        Guid? workspaceId,
        Guid? scopeId,
        string? details,
        CancellationToken cancellationToken)
    {
        try
        {
            var entry = AuditLogEntry.Create(
                action,
                targetType: scopeId is null ? "workspace" : "scope",
                outcome,
                occurredAtUtc: _timeProvider.GetUtcNow(),
                actorUserId: _currentUser.UserId,
                actorIdentifier: _currentUser.Email,
                targetId: scopeId?.ToString() ?? workspaceId?.ToString(),
                ipAddress: _requestContext.IpAddress,
                userAgent: _requestContext.UserAgent,
                details: workspaceId is null ? details : $"workspace={workspaceId};{details}");

            await _auditLog.WriteAsync(entry, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to write scope audit record for action {Action}.", action);
        }
    }

    private static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > 400 ? trimmed[..400] : trimmed;
    }

    private sealed record ScopeAccess(Guid UserId, WorkspaceRole Role, bool IsArchived);
}
