using System.Net;
using System.Net.Sockets;

namespace TraceForge.Domain.Scopes;

/// <summary>
/// Classifies destination addresses. Private, loopback, link-local, carrier-grade NAT, multicast,
/// documentation and other reserved ranges are denied unless an isolated lab configuration is
/// explicitly enabled and the matching scope carries the private-network restriction.
/// </summary>
public static class NetworkAddressClassifier
{
    private static readonly (string Network, int Prefix)[] ReservedIPv4Ranges =
    [
        ("0.0.0.0", 8),
        ("10.0.0.0", 8),
        ("100.64.0.0", 10),
        ("127.0.0.0", 8),
        ("169.254.0.0", 16),
        ("172.16.0.0", 12),
        ("192.0.0.0", 24),
        ("192.0.2.0", 24),
        ("192.88.99.0", 24),
        ("192.168.0.0", 16),
        ("198.18.0.0", 15),
        ("198.51.100.0", 24),
        ("203.0.113.0", 24),
        ("224.0.0.0", 4),
        ("240.0.0.0", 4),
    ];

    private static readonly (string Network, int Prefix)[] ReservedIPv6Ranges =
    [
        ("::", 128),
        ("::1", 128),
        ("64:ff9b::", 96),
        ("100::", 64),
        ("2001:2::", 48),
        ("2001:db8::", 32),
        ("fc00::", 7),
        ("fe80::", 10),
        ("ff00::", 8),
    ];

    public static bool IsPrivateOrReserved(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        var candidate = ScopeTargetNormalizer.Unmap(address);

        if (candidate.AddressFamily == AddressFamily.InterNetwork)
        {
            if (candidate.Equals(IPAddress.Broadcast) || candidate.Equals(IPAddress.Any))
            {
                return true;
            }

            foreach (var (network, prefix) in ReservedIPv4Ranges)
            {
                if (ScopeTargetNormalizer.CidrContains(IPAddress.Parse(network), prefix, candidate))
                {
                    return true;
                }
            }

            return false;
        }

        if (candidate.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return true;
        }

        foreach (var (network, prefix) in ReservedIPv6Ranges)
        {
            if (ScopeTargetNormalizer.CidrContains(IPAddress.Parse(network), prefix, candidate))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsPublicUnicast(IPAddress address) => !IsPrivateOrReserved(address);
}
