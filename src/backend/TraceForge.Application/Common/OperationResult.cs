namespace TraceForge.Application.Common;

/// <summary>Why an application operation could not be completed. Mapped to HTTP by the API layer.</summary>
public enum OperationFailure
{
    None = 0,

    /// <summary>The resource is missing or the caller has no access; the API returns 404 for both.</summary>
    NotFound = 1,

    /// <summary>The caller is a member but lacks the required role or permission.</summary>
    Forbidden = 2,

    /// <summary>The request conflicts with current state (duplicate, last owner, invalid transition).</summary>
    Conflict = 3,

    /// <summary>Input failed validation.</summary>
    Validation = 4,

    /// <summary>The workspace is archived and does not accept this operation.</summary>
    Archived = 5,

    /// <summary>The operation would remove or demote the last owner.</summary>
    LastOwner = 6,
}

public sealed record OperationResult<T>(
    bool Succeeded,
    T? Value,
    OperationFailure Failure,
    string? Error);

/// <summary>Factories for <see cref="OperationResult{T}"/>.</summary>
public static class OperationResults
{
    public static OperationResult<T> Success<T>(T value) =>
        new(true, value, OperationFailure.None, null);

    public static OperationResult<T> Fail<T>(OperationFailure failure, string error) =>
        new(false, default, failure, error);
}
