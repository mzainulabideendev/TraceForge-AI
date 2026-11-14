using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace TraceForge.Domain.Scopes;

/// <summary>
/// Parses and canonicalises scope targets. Normalisation is deterministic and happens before
/// storage, so duplicate or visually different spellings collapse to one value.
/// </summary>
public static class ScopeTargetNormalizer
{
    public const int MaxTargetLength = 512;
    public const int MaxHostnameLength = 253;
    public const int MaxLabelLength = 63;
    public const int MinCidrPrefixLength = 1;

    public static bool TryNormalize(
        ScopeType type,
        string? raw,
        out string normalized,
        out string? error)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "A target is required.";
            return false;
        }

        var candidate = raw.Trim();

        if (candidate.Length > MaxTargetLength)
        {
            error = $"A target must be at most {MaxTargetLength} characters.";
            return false;
        }

        return type switch
        {
            ScopeType.IPv4 => TryNormalizeIp(candidate, AddressFamily.InterNetwork, out normalized, out error),
            ScopeType.IPv6 => TryNormalizeIp(candidate, AddressFamily.InterNetworkV6, out normalized, out error),
            ScopeType.Cidr => TryNormalizeCidr(candidate, out normalized, out error),
            ScopeType.Hostname => TryNormalizeHostname(candidate, out normalized, out error),
            ScopeType.Origin => TryNormalizeOrigin(candidate, out normalized, out error),
            _ => Fail("Unknown scope type.", out normalized, out error),
        };
    }

    public static bool TryNormalizeIp(
        string? raw,
        AddressFamily family,
        out string normalized,
        out string? error)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return Fail("An IP address is required.", out normalized, out error);
        }

        var candidate = raw.Trim();

        // Zone indices ("fe80::1%eth0") are host-local and must never be persisted.
        if (candidate.Contains('%'))
        {
            return Fail("IP addresses must not contain a zone index.", out normalized, out error);
        }

        if (!IPAddress.TryParse(candidate, out var address))
        {
            return Fail("The value is not a valid IP address.", out normalized, out error);
        }

        if (address.AddressFamily != family)
        {
            var expected = family == AddressFamily.InterNetwork ? "IPv4" : "IPv6";
            return Fail($"The value is not a valid {expected} address.", out normalized, out error);
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return Fail("IPv4-mapped IPv6 addresses must be registered as IPv4 scopes.", out normalized, out error);
        }

        normalized = address.ToString();
        error = null;
        return true;
    }

    public static bool TryNormalizeCidr(string? raw, out string normalized, out string? error)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return Fail("A CIDR range is required.", out normalized, out error);
        }

        var candidate = raw.Trim();
        var slash = candidate.IndexOf('/');
        if (slash <= 0 || slash != candidate.LastIndexOf('/'))
        {
            return Fail("A CIDR range must be in the form 'address/prefix'.", out normalized, out error);
        }

        var addressPart = candidate[..slash];
        var prefixPart = candidate[(slash + 1)..];

        if (addressPart.Contains('%'))
        {
            return Fail("CIDR addresses must not contain a zone index.", out normalized, out error);
        }

        if (!IPAddress.TryParse(addressPart, out var address) || address.IsIPv4MappedToIPv6)
        {
            return Fail("The CIDR address is not valid.", out normalized, out error);
        }

        var maxPrefix = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;

        if (!int.TryParse(prefixPart, NumberStyles.None, CultureInfo.InvariantCulture, out var prefix) ||
            prefix < MinCidrPrefixLength ||
            prefix > maxPrefix)
        {
            return Fail(
                $"The CIDR prefix must be between {MinCidrPrefixLength} and {maxPrefix}.",
                out normalized,
                out error);
        }

        var network = Mask(address, prefix);
        normalized = $"{network}/{prefix}";
        error = null;
        return true;
    }

    public static bool TryNormalizeHostname(string? raw, out string normalized, out string? error)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return Fail("A hostname is required.", out normalized, out error);
        }

        var candidate = raw.Trim();

        if (candidate.Contains("://", StringComparison.Ordinal))
        {
            return Fail("Use the Origin scope type for URLs.", out normalized, out error);
        }

        if (candidate.Contains(':') || candidate.Contains('/') || candidate.Contains('?') ||
            candidate.Contains('#') || candidate.Contains('@') || candidate.Contains('\\'))
        {
            return Fail("A hostname must not contain a port, path, credentials or URL syntax.", out normalized, out error);
        }

        if (candidate.Any(char.IsWhiteSpace) || candidate.Any(char.IsControl))
        {
            return Fail("A hostname must not contain whitespace or control characters.", out normalized, out error);
        }

        string ascii;
        try
        {
            ascii = new IdnMapping().GetAscii(candidate);
        }
        catch (ArgumentException)
        {
            return Fail("The hostname contains an invalid internationalised label.", out normalized, out error);
        }

        ascii = ascii.TrimEnd('.').ToLowerInvariant();

        if (ascii.Length == 0 || ascii.Length > MaxHostnameLength)
        {
            return Fail($"A hostname must be between 1 and {MaxHostnameLength} characters.", out normalized, out error);
        }

        if (IPAddress.TryParse(ascii, out _))
        {
            return Fail("IP literals must use an IP scope type, not a hostname.", out normalized, out error);
        }

        var labels = ascii.Split('.');
        if (labels.Length < 2)
        {
            return Fail("A hostname must contain at least one dot (a registrable name).", out normalized, out error);
        }

        foreach (var label in labels)
        {
            if (label.Length is 0 or > MaxLabelLength)
            {
                return Fail("Each hostname label must be between 1 and 63 characters.", out normalized, out error);
            }

            if (label[0] == '-' || label[^1] == '-')
            {
                return Fail("Hostname labels must not start or end with a hyphen.", out normalized, out error);
            }

            foreach (var character in label)
            {
                var valid = character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-';
                if (!valid)
                {
                    return Fail("Hostnames may contain only letters, digits, hyphens and dots.", out normalized, out error);
                }
            }
        }

        normalized = ascii;
        error = null;
        return true;
    }

    public static bool TryNormalizeOrigin(string? raw, out string normalized, out string? error)
    {
        normalized = string.Empty;

        if (!TryParseOrigin(raw, out var origin, out error))
        {
            return false;
        }

        normalized = origin.Value;
        return true;
    }

    public static bool TryParseOrigin(string? raw, out OriginTarget origin, out string? error)
    {
        origin = default;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return Fail("An origin is required.", out error);
        }

        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri))
        {
            return Fail("The value is not an absolute URL.", out error);
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return Fail("Only http and https origins are supported.", out error);
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return Fail("Origins must not contain user information.", out error);
        }

        if (uri.AbsolutePath is not ("" or "/") || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return Fail("An origin must not contain a path, query or fragment.", out error);
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            return Fail("The origin has no host.", out error);
        }

        string host;
        var isIpLiteral = false;

        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            if (!IPAddress.TryParse(uri.Host, out var address))
            {
                return Fail("The origin host is not a valid IP address.", out error);
            }

            if (address.IsIPv4MappedToIPv6)
            {
                return Fail("IPv4-mapped IPv6 origins are not supported.", out error);
            }

            host = address.ToString();
            isIpLiteral = true;
        }
        else
        {
            if (!TryNormalizeHostname(uri.Host, out host, out var hostnameError))
            {
                return Fail(hostnameError ?? "The origin host is not a valid hostname.", out error);
            }
        }

        var scheme = uri.Scheme.ToLowerInvariant();
        var defaultPort = scheme == Uri.UriSchemeHttps ? 443 : 80;
        var port = uri.IsDefaultPort ? defaultPort : uri.Port;

        if (port is < 1 or > 65535)
        {
            return Fail("The origin port is out of range.", out error);
        }

        origin = new OriginTarget(scheme, host, port, isIpLiteral);
        error = null;
        return true;
    }

    public static bool TryParseCidr(string normalized, out IPAddress network, out int prefix)
    {
        network = IPAddress.None;
        prefix = 0;

        var slash = normalized.IndexOf('/');
        if (slash <= 0)
        {
            return false;
        }

        if (!IPAddress.TryParse(normalized[..slash], out var address))
        {
            return false;
        }

        if (!int.TryParse(normalized[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out prefix))
        {
            return false;
        }

        var max = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        if (prefix < MinCidrPrefixLength || prefix > max)
        {
            return false;
        }

        network = address;
        return true;
    }

    /// <summary>True when <paramref name="candidate"/> falls inside the network/prefix.</summary>
    public static bool CidrContains(IPAddress network, int prefix, IPAddress candidate)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(candidate);

        var normalizedCandidate = Unmap(candidate);
        var normalizedNetwork = Unmap(network);

        if (normalizedCandidate.AddressFamily != normalizedNetwork.AddressFamily)
        {
            return false;
        }

        var networkBytes = normalizedNetwork.GetAddressBytes();
        var candidateBytes = normalizedCandidate.GetAddressBytes();

        var fullBytes = prefix / 8;
        var remainingBits = prefix % 8;

        for (var i = 0; i < fullBytes; i++)
        {
            if (networkBytes[i] != candidateBytes[i])
            {
                return false;
            }
        }

        if (remainingBits == 0)
        {
            return true;
        }

        var mask = (byte)(0xFF << (8 - remainingBits));
        return (networkBytes[fullBytes] & mask) == (candidateBytes[fullBytes] & mask);
    }

    /// <summary>Converts IPv4-mapped IPv6 addresses to IPv4 so range checks use one representation.</summary>
    public static IPAddress Unmap(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    private static IPAddress Mask(IPAddress address, int prefix)
    {
        var bytes = address.GetAddressBytes();
        var fullBytes = prefix / 8;
        var remainingBits = prefix % 8;

        if (remainingBits > 0)
        {
            var mask = (byte)(0xFF << (8 - remainingBits));
            bytes[fullBytes] &= mask;
            fullBytes++;
        }

        for (var i = fullBytes; i < bytes.Length; i++)
        {
            bytes[i] = 0;
        }

        return new IPAddress(bytes);
    }

    private static bool Fail(string message, out string normalized, out string? error)
    {
        normalized = string.Empty;
        error = message;
        return false;
    }

    private static bool Fail(string message, out string? error)
    {
        error = message;
        return false;
    }
}

/// <summary>A canonical http/https origin: scheme, host and explicit port.</summary>
public readonly record struct OriginTarget(string Scheme, string Host, int Port, bool IsIpLiteral)
{
    public string Value
    {
        get
        {
            var host = IsIpLiteral && Host.Contains(':') ? $"[{Host}]" : Host;
            return $"{Scheme}://{host}:{Port}";
        }
    }

    public override string ToString() => Value;
}
