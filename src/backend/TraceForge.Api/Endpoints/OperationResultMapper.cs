using TraceForge.Application.Common;

namespace TraceForge.Api.Endpoints;

/// <summary>Maps application failures to HTTP problem responses without leaking existence.</summary>
internal static class OperationResultMapper
{
    public static IResult ToProblem<T>(OperationResult<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var (status, title) = result.Failure switch
        {
            OperationFailure.NotFound => (StatusCodes.Status404NotFound, "Resource not found"),
            OperationFailure.Forbidden => (StatusCodes.Status403Forbidden, "Forbidden"),
            OperationFailure.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
            OperationFailure.Archived => (StatusCodes.Status409Conflict, "Workspace archived"),
            OperationFailure.LastOwner => (StatusCodes.Status409Conflict, "Last owner protected"),
            OperationFailure.Validation => (StatusCodes.Status400BadRequest, "Invalid input"),
            _ => (StatusCodes.Status400BadRequest, "Request failed"),
        };

        return Results.Problem(
            statusCode: status,
            title: title,
            detail: result.Error ?? "The request could not be completed.");
    }
}
