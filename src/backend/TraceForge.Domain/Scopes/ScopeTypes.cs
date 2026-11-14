namespace TraceForge.Domain.Scopes;

/// <summary>What kind of target an authorized scope describes.</summary>
public enum ScopeType
{
    IPv4 = 1,
    IPv6 = 2,
    Cidr = 3,
    Hostname = 4,
    Origin = 5,
}

/// <summary>
/// Approval lifecycle. A newly registered scope is always <see cref="Pending"/>; registration never
/// approves a target automatically.
/// </summary>
public enum ScopeApprovalStatus
{
    Pending = 1,
    Approved = 2,
    Revoked = 3,
    Expired = 4,
}

/// <summary>
/// Optional restrictions that narrow what an approved scope permits. Restrictions are stored in a
/// canonical, sorted, comma-separated form.
/// </summary>
public static class ScopeRestrictions
{
    /// <summary>
    /// Required, together with the deployment-wide <c>TraceForge:Scopes:AllowPrivateNetworks</c>
    /// switch, before any private, loopback, link-local or reserved address may be contacted.
    /// </summary>
    public const string PrivateNetwork = "private-network";

    public const int MaxLength = 200;

    private static readonly string[] Allowed = [PrivateNetwork];

    public static bool TryCanonicalize(
        IEnumerable<string>? restrictions,
        out string canonical,
        out string? error)
    {
        var tokens = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var value in restrictions ?? [])
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var token = value.Trim().ToLowerInvariant();
            if (!Allowed.Contains(token, StringComparer.Ordinal))
            {
                canonical = string.Empty;
                error = $"Unknown scope restriction '{value}'. Allowed values: {string.Join(", ", Allowed)}.";
                return false;
            }

            tokens.Add(token);
        }

        canonical = string.Join(",", tokens);

        if (canonical.Length > MaxLength)
        {
            error = $"Scope restrictions must be at most {MaxLength} characters.";
            return false;
        }

        error = null;
        return true;
    }

    public static IReadOnlyList<string> Parse(string? canonical)
    {
        if (string.IsNullOrWhiteSpace(canonical))
        {
            return [];
        }

        return canonical
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    public static bool Has(string? canonical, string restriction) =>
        Parse(canonical).Contains(restriction, StringComparer.Ordinal);
}
