namespace TraceForge.Domain.Identity;

/// <summary>
/// Role names used by the product. Persisted, therefore treated as stable identifiers.
/// </summary>
public static class ApplicationRoles
{
    /// <summary>Full control over users, workspaces, scopes and AI provider configuration.</summary>
    public const string Administrator = "Administrator";

    /// <summary>May run authorized assessments and produce findings; cannot manage users.</summary>
    public const string Analyst = "Analyst";

    public static readonly IReadOnlyList<string> All = [Administrator, Analyst];

    public static bool IsKnown(string role) =>
        string.Equals(role, Administrator, StringComparison.Ordinal) ||
        string.Equals(role, Analyst, StringComparison.Ordinal);
}
