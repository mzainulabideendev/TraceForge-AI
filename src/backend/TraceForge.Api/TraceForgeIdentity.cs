namespace TraceForge.Api;

/// <summary>
/// Stable identity metadata for the running TraceForge AI instance.
/// Version is read from the assembly; it is never hard-coded per endpoint.
/// </summary>
public static class TraceForgeIdentity
{
    public const string Name = "TraceForge AI";

    public const string ApiDocumentName = "v1";

    public static string InformationalVersion { get; } =
        typeof(TraceForgeIdentity).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), inherit: false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion
        ?? typeof(TraceForgeIdentity).Assembly.GetName().Version?.ToString()
        ?? "unknown";
}
