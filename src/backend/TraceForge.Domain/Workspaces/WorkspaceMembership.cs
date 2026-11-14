namespace TraceForge.Domain.Workspaces;

/// <summary>
/// Grants one user a role inside one workspace. A (workspace, user) pair is unique at the database
/// level; there is no implicit membership through e-mail domain, global role or workspace id.
/// </summary>
public sealed class WorkspaceMembership
{
    private WorkspaceMembership()
    {
    }

    private WorkspaceMembership(
        Guid id,
        Guid workspaceId,
        Guid userId,
        WorkspaceRole role,
        DateTimeOffset now)
    {
        Id = id;
        WorkspaceId = workspaceId;
        UserId = userId;
        Role = role;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    public Guid Id { get; private set; }

    public Guid WorkspaceId { get; private set; }

    public Guid UserId { get; private set; }

    public WorkspaceRole Role { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static WorkspaceMembership Create(
        Guid workspaceId,
        Guid userId,
        WorkspaceRole role,
        DateTimeOffset now)
    {
        if (workspaceId == Guid.Empty)
        {
            throw new ArgumentException("A workspace is required.", nameof(workspaceId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A user is required.", nameof(userId));
        }

        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown workspace role.");
        }

        if (now.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamps must be UTC.", nameof(now));
        }

        return new WorkspaceMembership(Guid.NewGuid(), workspaceId, userId, role, now);
    }

    public void ChangeRole(WorkspaceRole role, DateTimeOffset now)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown workspace role.");
        }

        if (now.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamps must be UTC.", nameof(now));
        }

        if (Role == role)
        {
            return;
        }

        Role = role;
        UpdatedAtUtc = now;
    }
}
