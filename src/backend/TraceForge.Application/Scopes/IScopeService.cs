using TraceForge.Application.Common;
using TraceForge.Domain.Scopes;

namespace TraceForge.Application.Scopes;

public sealed record RegisterScopeCommand(
    ScopeType Type,
    string Target,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset ExpiresAtUtc,
    IReadOnlyCollection<string> Restrictions);

public sealed record ApproveScopeCommand(string ApprovalReference);

public sealed record RevokeScopeCommand(string? Reason);

public sealed record UpdateScopeRestrictionsCommand(IReadOnlyCollection<string> Restrictions);

public sealed record ScopeRecord(
    Guid Id,
    Guid WorkspaceId,
    ScopeType Type,
    string Target,
    string Status,
    string? ApprovalReference,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAtUtc,
    Guid? RevokedByUserId,
    DateTimeOffset? RevokedAtUtc,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset ExpiresAtUtc,
    IReadOnlyList<string> Restrictions,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    bool CurrentlyUsable);

/// <summary>
/// Authorized-scope management. Registration never approves; approval is always an explicit,
/// audited action performed by an Owner or Administrator.
/// </summary>
public interface IScopeService
{
    Task<OperationResult<ScopeRecord>> RegisterAsync(
        Guid workspaceId,
        RegisterScopeCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<IReadOnlyList<ScopeRecord>>> ListAsync(
        Guid workspaceId,
        CancellationToken cancellationToken);

    Task<OperationResult<ScopeRecord>> GetAsync(
        Guid workspaceId,
        Guid scopeId,
        CancellationToken cancellationToken);

    Task<OperationResult<ScopeRecord>> ApproveAsync(
        Guid workspaceId,
        Guid scopeId,
        ApproveScopeCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<ScopeRecord>> RevokeAsync(
        Guid workspaceId,
        Guid scopeId,
        RevokeScopeCommand command,
        CancellationToken cancellationToken);

    Task<OperationResult<ScopeRecord>> ExpireAsync(
        Guid workspaceId,
        Guid scopeId,
        CancellationToken cancellationToken);

    Task<OperationResult<ScopeRecord>> UpdateRestrictionsAsync(
        Guid workspaceId,
        Guid scopeId,
        UpdateScopeRestrictionsCommand command,
        CancellationToken cancellationToken);
}
