using TraceForge.Application.Workspaces;
using TraceForge.Domain.Workspaces;

namespace TraceForge.Api.Endpoints;

public static class WorkspaceEndpoints
{
    public sealed record CreateWorkspaceRequest(string? Name, string? Description);

    public sealed record UpdateWorkspaceRequest(string? Name, string? Description);

    public sealed record AddWorkspaceMemberRequest(string? Email, string? Role);

    public sealed record ChangeWorkspaceMemberRoleRequest(string? Role);

    public static RouteGroupBuilder MapWorkspaceEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        var workspaces = api.MapGroup("/workspaces")
            .RequireAuthorization()
            .WithTags("Workspaces");

        workspaces.MapPost("", static async (
                CreateWorkspaceRequest request,
                IWorkspaceService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.CreateAsync(
                    new CreateWorkspaceCommand(request.Name ?? string.Empty, request.Description),
                    cancellationToken);

                return result.Succeeded
                    ? Results.Created($"/api/workspaces/{result.Value!.Id}", result.Value)
                    : OperationResultMapper.ToProblem(result);
            })
            .WithName("CreateWorkspace")
            .WithSummary("Creates a workspace; the caller becomes its owner.");

        workspaces.MapGet("", static async (
                IWorkspaceService service,
                CancellationToken cancellationToken) =>
            {
                var items = await service.ListAccessibleAsync(cancellationToken);
                return Results.Ok(items);
            })
            .WithName("ListWorkspaces")
            .WithSummary("Lists only the workspaces the caller is a member of.");

        workspaces.MapGet("/{workspaceId:guid}", static async (
                Guid workspaceId,
                IWorkspaceService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetAsync(workspaceId, cancellationToken);
                return result.Succeeded ? Results.Ok(result.Value) : OperationResultMapper.ToProblem(result);
            })
            .WithName("GetWorkspace")
            .WithSummary("Returns workspace details. Non-members receive 404.");

        workspaces.MapPut("/{workspaceId:guid}", static async (
                Guid workspaceId,
                UpdateWorkspaceRequest request,
                IWorkspaceService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.UpdateAsync(
                    workspaceId,
                    new UpdateWorkspaceCommand(request.Name ?? string.Empty, request.Description),
                    cancellationToken);

                return result.Succeeded ? Results.Ok(result.Value) : OperationResultMapper.ToProblem(result);
            })
            .WithName("UpdateWorkspace")
            .WithSummary("Updates workspace details. Owner or Administrator only.");

        workspaces.MapPost("/{workspaceId:guid}/archive", static async (
                Guid workspaceId,
                IWorkspaceService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.ArchiveAsync(workspaceId, cancellationToken);
                return result.Succeeded ? Results.Ok(result.Value) : OperationResultMapper.ToProblem(result);
            })
            .WithName("ArchiveWorkspace")
            .WithSummary("Archives a workspace. Owner only; archived workspaces reject mutations.");

        workspaces.MapGet("/{workspaceId:guid}/members", static async (
                Guid workspaceId,
                IWorkspaceService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.ListMembersAsync(workspaceId, cancellationToken);
                return result.Succeeded ? Results.Ok(result.Value) : OperationResultMapper.ToProblem(result);
            })
            .WithName("ListWorkspaceMembers")
            .WithSummary("Lists workspace members. Members only.");

        workspaces.MapPost("/{workspaceId:guid}/members", static async (
                Guid workspaceId,
                AddWorkspaceMemberRequest request,
                IWorkspaceService service,
                CancellationToken cancellationToken) =>
            {
                if (!TryParseRole(request.Role, out var role))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Invalid input",
                        detail: "Role must be one of: Owner, Administrator, Analyst.");
                }

                var result = await service.AddMemberAsync(
                    workspaceId,
                    new AddWorkspaceMemberCommand(request.Email ?? string.Empty, role),
                    cancellationToken);

                return result.Succeeded
                    ? Results.Created($"/api/workspaces/{workspaceId}/members", result.Value)
                    : OperationResultMapper.ToProblem(result);
            })
            .WithName("AddWorkspaceMember")
            .WithSummary("Adds a member. Owner or Administrator; only an owner may grant Owner.");

        workspaces.MapPatch("/{workspaceId:guid}/members/{userId:guid}", static async (
                Guid workspaceId,
                Guid userId,
                ChangeWorkspaceMemberRoleRequest request,
                IWorkspaceService service,
                CancellationToken cancellationToken) =>
            {
                if (!TryParseRole(request.Role, out var role))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Invalid input",
                        detail: "Role must be one of: Owner, Administrator, Analyst.");
                }

                var result = await service.ChangeMemberRoleAsync(
                    workspaceId,
                    userId,
                    new ChangeWorkspaceMemberRoleCommand(role),
                    cancellationToken);

                return result.Succeeded ? Results.Ok(result.Value) : OperationResultMapper.ToProblem(result);
            })
            .WithName("ChangeWorkspaceMemberRole")
            .WithSummary("Changes a member role. The last owner cannot be demoted.");

        workspaces.MapDelete("/{workspaceId:guid}/members/{userId:guid}", static async (
                Guid workspaceId,
                Guid userId,
                IWorkspaceService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.RemoveMemberAsync(workspaceId, userId, cancellationToken);
                return result.Succeeded ? Results.NoContent() : OperationResultMapper.ToProblem(result);
            })
            .WithName("RemoveWorkspaceMember")
            .WithSummary("Removes a member. The last owner cannot be removed.");

        return api;
    }

    private static bool TryParseRole(string? value, out WorkspaceRole role) =>
        WorkspaceRoles.TryParse(value, out role);
}
