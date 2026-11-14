using TraceForge.Application.Scopes;
using TraceForge.Domain.Scopes;

namespace TraceForge.Api.Endpoints;

public static class ScopeEndpoints
{
    private const int DefaultValidityDays = 90;

    public sealed record RegisterScopeRequest(
        string? Type,
        string? Target,
        DateTimeOffset? ValidFromUtc,
        DateTimeOffset? ExpiresAtUtc,
        string[]? Restrictions);

    public sealed record ApproveScopeRequest(string? ApprovalReference);

    public sealed record RevokeScopeRequest(string? Reason);

    public sealed record UpdateScopeRestrictionsRequest(string[]? Restrictions);

    public sealed record EvaluateScopeRequest(string? Target, string? Scheme, int? Port);

    public sealed record ScopeEvaluationResponse(
        bool Allowed,
        string Reason,
        string Detail,
        Guid? MatchedScopeId,
        IReadOnlyList<string> PinnedAddresses);

    public static RouteGroupBuilder MapScopeEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        var scopes = api.MapGroup("/workspaces/{workspaceId:guid}/scopes")
            .RequireAuthorization()
            .WithTags("Authorized scopes");

        scopes.MapPost("", static async (
                Guid workspaceId,
                RegisterScopeRequest request,
                IScopeService service,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                if (!TryParseType(request.Type, out var type))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Invalid input",
                        detail: "Type must be one of: IPv4, IPv6, Cidr, Hostname, Origin.");
                }

                var now = timeProvider.GetUtcNow();
                var result = await service.RegisterAsync(
                    workspaceId,
                    new RegisterScopeCommand(
                        type,
                        request.Target ?? string.Empty,
                        request.ValidFromUtc ?? now,
                        request.ExpiresAtUtc ?? now.AddDays(DefaultValidityDays),
                        request.Restrictions ?? []),
                    cancellationToken);

                return result.Succeeded
                    ? Results.Created($"/api/workspaces/{workspaceId}/scopes/{result.Value!.Id}", result.Value)
                    : OperationResultMapper.ToProblem(result);
            })
            .WithName("RegisterScope")
            .WithSummary("Registers a scope in Pending state. Registration never approves a target.");

        scopes.MapGet("", static async (
                Guid workspaceId,
                IScopeService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.ListAsync(workspaceId, cancellationToken);
                return result.Succeeded ? Results.Ok(result.Value) : OperationResultMapper.ToProblem(result);
            })
            .WithName("ListScopes")
            .WithSummary("Lists authorized scopes in a workspace. Members only.");

        scopes.MapGet("/{scopeId:guid}", static async (
                Guid workspaceId,
                Guid scopeId,
                IScopeService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetAsync(workspaceId, scopeId, cancellationToken);
                return result.Succeeded ? Results.Ok(result.Value) : OperationResultMapper.ToProblem(result);
            })
            .WithName("GetScope")
            .WithSummary("Returns one scope and its approval state.");

        scopes.MapPost("/{scopeId:guid}/approve", static async (
                Guid workspaceId,
                Guid scopeId,
                ApproveScopeRequest request,
                IScopeService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.ApproveAsync(
                    workspaceId,
                    scopeId,
                    new ApproveScopeCommand(request.ApprovalReference ?? string.Empty),
                    cancellationToken);

                return result.Succeeded ? Results.Ok(result.Value) : OperationResultMapper.ToProblem(result);
            })
            .WithName("ApproveScope")
            .WithSummary("Explicitly approves a pending scope. Owner or Administrator; evidence is required.");

        scopes.MapPost("/{scopeId:guid}/revoke", static async (
                Guid workspaceId,
                Guid scopeId,
                RevokeScopeRequest request,
                IScopeService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.RevokeAsync(
                    workspaceId,
                    scopeId,
                    new RevokeScopeCommand(request.Reason),
                    cancellationToken);

                return result.Succeeded ? Results.Ok(result.Value) : OperationResultMapper.ToProblem(result);
            })
            .WithName("RevokeScope")
            .WithSummary("Revokes a scope immediately. A revoked scope cannot be re-approved.");

        scopes.MapPost("/{scopeId:guid}/expire", static async (
                Guid workspaceId,
                Guid scopeId,
                IScopeService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.ExpireAsync(workspaceId, scopeId, cancellationToken);
                return result.Succeeded ? Results.Ok(result.Value) : OperationResultMapper.ToProblem(result);
            })
            .WithName("ExpireScope")
            .WithSummary("Marks a scope expired once its expiration time has passed.");

        scopes.MapPatch("/{scopeId:guid}/restrictions", static async (
                Guid workspaceId,
                Guid scopeId,
                UpdateScopeRestrictionsRequest request,
                IScopeService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.UpdateRestrictionsAsync(
                    workspaceId,
                    scopeId,
                    new UpdateScopeRestrictionsCommand(request.Restrictions ?? []),
                    cancellationToken);

                return result.Succeeded ? Results.Ok(result.Value) : OperationResultMapper.ToProblem(result);
            })
            .WithName("UpdateScopeRestrictions")
            .WithSummary("Updates restrictions; an approved scope returns to Pending and must be re-approved.");

        scopes.MapPost("/evaluate", static async (
                Guid workspaceId,
                EvaluateScopeRequest request,
                IScopeValidationService validation,
                CancellationToken cancellationToken) =>
            {
                var evaluation = await validation.EvaluateAsync(
                    workspaceId,
                    new ScopeCheckRequest(
                        request.Target ?? string.Empty,
                        string.IsNullOrWhiteSpace(request.Scheme) ? null : request.Scheme,
                        request.Port),
                    cancellationToken);

                return Results.Ok(new ScopeEvaluationResponse(
                    evaluation.Allowed,
                    evaluation.Reason.ToString(),
                    evaluation.Detail,
                    evaluation.MatchedScopeId,
                    evaluation.PinnedAddresses.Select(address => address.ToString()).ToArray()));
            })
            .WithName("EvaluateScope")
            .WithSummary("Evaluates a destination against approved scopes. Does not connect to the target.");

        return api;
    }

    private static bool TryParseType(string? value, out ScopeType type) =>
        Enum.TryParse(value, ignoreCase: true, out type) && Enum.IsDefined(type);
}
