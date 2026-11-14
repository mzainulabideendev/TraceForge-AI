using TraceForge.Api.Security;
using TraceForge.Application.Accounts;
using TraceForge.Application.Audit;
using TraceForge.Application.Common;
using TraceForge.Domain.Audit;

namespace TraceForge.Api.Endpoints;

public static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdminEndpoints(this RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);

        var admin = api.MapGroup("/admin")
            .RequireAuthorization(AuthorizationPolicies.AdministratorOnly)
            .WithTags("Administration");

        admin.MapGet("/users", static async (
                IAccountService accounts,
                IAuditLogWriter auditLog,
                TimeProvider timeProvider,
                int page = 1,
                int pageSize = Paging.DefaultPageSize,
                CancellationToken cancellationToken = default) =>
            {
                var result = await accounts.ListAsync(page, pageSize, cancellationToken);

                await auditLog.WriteAsync(
                    AuditLogEntry.Create(
                        AuditActions.UsersListed,
                        targetType: "user-collection",
                        AuditOutcome.Success,
                        occurredAtUtc: timeProvider.GetUtcNow(),
                        details: $"page={result.Page};pageSize={result.PageSize};total={result.TotalCount}"),
                    cancellationToken);

                return TypedResults.Ok(result);
            })
            .WithName("ListUsers")
            .WithSummary("Lists accounts. Administrator only.");

        admin.MapGet("/audit", static async (
                IAuditLogReader auditReader,
                IAuditLogWriter auditLog,
                TimeProvider timeProvider,
                int page = 1,
                int pageSize = Paging.DefaultPageSize,
                CancellationToken cancellationToken = default) =>
            {
                var result = await auditReader.ReadAsync(page, pageSize, cancellationToken);

                await auditLog.WriteAsync(
                    AuditLogEntry.Create(
                        AuditActions.AuditLogQueried,
                        targetType: "audit-log",
                        AuditOutcome.Success,
                        occurredAtUtc: timeProvider.GetUtcNow(),
                        details: $"page={result.Page};pageSize={result.PageSize}"),
                    cancellationToken);

                return TypedResults.Ok(result);
            })
            .WithName("ReadAuditLog")
            .WithSummary("Reads the security audit log, newest first. Administrator only.");

        return api;
    }
}
