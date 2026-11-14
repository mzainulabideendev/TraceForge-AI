using System.Net;
using Microsoft.Extensions.Logging;
using TraceForge.Application.Scopes;
using TraceForge.Domain.Scopes;

namespace TraceForge.Infrastructure.Scopes;

/// <summary>
/// Production DNS resolver. It returns every address the name resolves to so the validation policy
/// can require an approval for each one; resolution itself is not an authorization decision.
/// </summary>
internal sealed class SystemDnsResolver : IDnsResolver
{
    private readonly ILogger<SystemDnsResolver> _logger;

    public SystemDnsResolver(ILogger<SystemDnsResolver> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(
        string hostname,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostname);

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(hostname, cancellationToken);

            return addresses
                .Select(ScopeTargetNormalizer.Unmap)
                .Distinct()
                .ToArray();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "DNS resolution failed for an authorized-scope evaluation.");
            return [];
        }
    }
}
