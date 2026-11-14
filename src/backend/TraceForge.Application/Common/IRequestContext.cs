namespace TraceForge.Application.Common;

/// <summary>
/// Metadata about the current HTTP request, used for audit records.
/// Implemented in the API/infrastructure layer; deliberately not an HttpContext dependency here.
/// </summary>
public interface IRequestContext
{
    string? IpAddress { get; }

    string? UserAgent { get; }
}
