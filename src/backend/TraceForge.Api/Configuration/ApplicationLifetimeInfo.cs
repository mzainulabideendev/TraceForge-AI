namespace TraceForge.Api.Configuration;

/// <summary>
/// Captured once when the process starts. Used to report real uptime, not a synthetic value.
/// </summary>
public sealed class ApplicationLifetimeInfo
{
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

    public TimeSpan Uptime => DateTimeOffset.UtcNow - StartedAt;
}
