namespace TraceForge.Api.Configuration;

/// <summary>
/// Root configuration for the TraceForge API. Bound from the "TraceForge" configuration section
/// and validated at startup so a misconfigured deployment fails fast instead of running degraded.
/// </summary>
public sealed class TraceForgeOptions
{
    public const string SectionName = "TraceForge";

    public string InstanceName { get; init; } = "TraceForge AI";

    public RateLimitingOptions RateLimiting { get; init; } = new();
}

/// <summary>
/// Per-client fixed-window request limiting for the /api surface.
/// </summary>
public sealed class RateLimitingOptions
{
    public const int MinPermitLimit = 1;
    public const int MaxPermitLimit = 100_000;
    public const int MinWindowSeconds = 1;
    public const int MaxWindowSeconds = 3_600;
    public const int MinQueueLimit = 0;

    public int PermitLimit { get; init; } = 120;

    public int WindowSeconds { get; init; } = 60;

    public int QueueLimit { get; init; }

    /// <summary>Sensitive credential endpoints get a much tighter budget per client.</summary>
    public int AuthPermitLimit { get; init; } = 10;

    public int AuthWindowSeconds { get; init; } = 300;
}
