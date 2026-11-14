namespace TraceForge.Domain.Workspaces;

public enum WorkspaceStatus
{
    Active = 1,
    Archived = 2,
}

/// <summary>
/// A tenant boundary. Every workspace-scoped resource belongs to exactly one workspace, and access
/// requires an explicit membership row. Knowing a workspace id grants nothing.
/// </summary>
public sealed class Workspace
{
    public const int NameMinLength = 2;
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 1000;

    private Workspace()
    {
    }

    private Workspace(Guid id, string name, string? description, Guid createdByUserId, DateTimeOffset now)
    {
        Id = id;
        Name = name;
        Description = description;
        CreatedByUserId = createdByUserId;
        Status = WorkspaceStatus.Active;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public WorkspaceStatus Status { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset? ArchivedAtUtc { get; private set; }

    public bool IsArchived => Status == WorkspaceStatus.Archived;

    public static Workspace Create(
        string name,
        string? description,
        Guid createdByUserId,
        DateTimeOffset now)
    {
        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException("A creator is required.", nameof(createdByUserId));
        }

        RequireUtc(now, nameof(now));

        return new Workspace(
            Guid.NewGuid(),
            NormalizeName(name),
            NormalizeDescription(description),
            createdByUserId,
            now);
    }

    public void Update(string name, string? description, DateTimeOffset now)
    {
        RequireActive();
        RequireUtc(now, nameof(now));

        Name = NormalizeName(name);
        Description = NormalizeDescription(description);
        UpdatedAtUtc = now;
    }

    public void Archive(DateTimeOffset now)
    {
        RequireUtc(now, nameof(now));

        if (IsArchived)
        {
            return;
        }

        Status = WorkspaceStatus.Archived;
        ArchivedAtUtc = now;
        UpdatedAtUtc = now;
    }

    public static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException($"A workspace name is required.", nameof(name));
        }

        var trimmed = name.Trim();

        if (trimmed.Any(char.IsControl))
        {
            throw new ArgumentException("A workspace name must not contain control characters.", nameof(name));
        }

        if (trimmed.Length is < NameMinLength or > NameMaxLength)
        {
            throw new ArgumentException(
                $"A workspace name must be between {NameMinLength} and {NameMaxLength} characters.",
                nameof(name));
        }

        return trimmed;
    }

    public static string? NormalizeDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var trimmed = description.Trim();

        if (trimmed.Any(char.IsControl))
        {
            throw new ArgumentException("A workspace description must not contain control characters.", nameof(description));
        }

        if (trimmed.Length > DescriptionMaxLength)
        {
            throw new ArgumentException(
                $"A workspace description must be at most {DescriptionMaxLength} characters.",
                nameof(description));
        }

        return trimmed;
    }

    private void RequireActive()
    {
        if (IsArchived)
        {
            throw new InvalidOperationException("An archived workspace cannot be modified.");
        }
    }

    private static void RequireUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamps must be UTC.", parameterName);
        }
    }
}
