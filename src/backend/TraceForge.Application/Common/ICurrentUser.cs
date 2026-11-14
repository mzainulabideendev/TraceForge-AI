namespace TraceForge.Application.Common;

/// <summary>
/// The authenticated caller, resolved from the session cookie. Workspace services use this instead
/// of trusting user ids supplied in request bodies or routes.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    string? Email { get; }
}
