using Microsoft.AspNetCore.Identity;

namespace TraceForge.Infrastructure.Persistence;

/// <summary>
/// Application user. Extends Identity with a display name, creation time and last login so the
/// UI can present real account state without inventing values.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public const int DisplayNameMaxLength = 120;

    public ApplicationUser()
    {
        Id = Guid.NewGuid();
    }

    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? LastLoginAtUtc { get; set; }
}
