using System.Net;
using System.Net.Sockets;
using TraceForge.Domain.Scopes;

namespace TraceForge.Api.Tests;

public sealed class ScopeNormalizationTests
{
    [Theory]
    [InlineData("8.8.8.8", "8.8.8.8")]
    [InlineData(" 1.1.1.1 ", "1.1.1.1")]
    public void IPv4_Normalizes(string input, string expected)
    {
        Assert.True(ScopeTargetNormalizer.TryNormalize(ScopeType.IPv4, input, out var normalized, out var error), error);
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void IPv4_RejectsIPv6Literal()
    {
        Assert.False(ScopeTargetNormalizer.TryNormalize(ScopeType.IPv4, "2606:4700:4700::1111", out _, out _));
    }

    [Fact]
    public void IPv6_RejectsMappedIPv4()
    {
        Assert.False(ScopeTargetNormalizer.TryNormalize(ScopeType.IPv6, "::ffff:8.8.8.8", out _, out var error));
        Assert.Contains("IPv4", error);
    }

    [Fact]
    public void IPv6_RejectsZoneIndex()
    {
        Assert.False(ScopeTargetNormalizer.TryNormalize(ScopeType.IPv6, "fe80::1%eth0", out _, out _));
    }

    [Fact]
    public void IPv6_NormalizesCompressedForm()
    {
        Assert.True(ScopeTargetNormalizer.TryNormalize(
            ScopeType.IPv6,
            "2606:4700:4700:0000:0000:0000:0000:1111",
            out var normalized,
            out var error), error);

        Assert.Equal("2606:4700:4700::1111", normalized);
    }

    [Fact]
    public void Cidr_MasksHostBitsAndKeepsPrefix()
    {
        Assert.True(ScopeTargetNormalizer.TryNormalize(ScopeType.Cidr, "192.168.1.130/24", out var normalized, out var error), error);
        Assert.Equal("192.168.1.0/24", normalized);
    }

    [Fact]
    public void Cidr_RejectsZeroPrefix()
    {
        Assert.False(ScopeTargetNormalizer.TryNormalize(ScopeType.Cidr, "0.0.0.0/0", out _, out _));
    }

    [Fact]
    public void Cidr_RejectsPrefixOutOfRange()
    {
        Assert.False(ScopeTargetNormalizer.TryNormalize(ScopeType.Cidr, "8.8.8.0/33", out _, out _));
        Assert.False(ScopeTargetNormalizer.TryNormalize(ScopeType.Cidr, "2606:4700::/129", out _, out _));
    }

    [Fact]
    public void Cidr_EvaluatesMembershipForBothFamilies()
    {
        Assert.True(ScopeTargetNormalizer.TryParseCidr("8.8.8.0/24", out var v4Network, out var v4Prefix));
        Assert.True(ScopeTargetNormalizer.CidrContains(v4Network, v4Prefix, IPAddress.Parse("8.8.8.255")));
        Assert.False(ScopeTargetNormalizer.CidrContains(v4Network, v4Prefix, IPAddress.Parse("8.8.9.1")));

        Assert.True(ScopeTargetNormalizer.TryParseCidr("2606:4700::/32", out var v6Network, out var v6Prefix));
        Assert.True(ScopeTargetNormalizer.CidrContains(v6Network, v6Prefix, IPAddress.Parse("2606:4700:4700::1111")));
        Assert.False(ScopeTargetNormalizer.CidrContains(v6Network, v6Prefix, IPAddress.Parse("2607:4700::1")));
    }

    [Fact]
    public void Cidr_DoesNotMixFamilies()
    {
        Assert.True(ScopeTargetNormalizer.TryParseCidr("8.8.8.0/24", out var network, out var prefix));
        Assert.False(ScopeTargetNormalizer.CidrContains(network, prefix, IPAddress.Parse("2606:4700::1")));
    }

    [Theory]
    [InlineData("Example.COM", "example.com")]
    [InlineData("example.com.", "example.com")]
    [InlineData("Bücher.example", "xn--bcher-kva.example")]
    public void Hostname_Normalizes(string input, string expected)
    {
        Assert.True(ScopeTargetNormalizer.TryNormalize(ScopeType.Hostname, input, out var normalized, out var error), error);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("example.com:443")]
    [InlineData("https://example.com")]
    [InlineData("user@example.com")]
    [InlineData("example.com/path")]
    [InlineData("exa mple.com")]
    [InlineData("localhost")]
    [InlineData("8.8.8.8")]
    [InlineData("-example.com")]
    public void Hostname_RejectsNonHostnameSyntax(string input)
    {
        Assert.False(ScopeTargetNormalizer.TryNormalize(ScopeType.Hostname, input, out _, out _));
    }

    [Theory]
    [InlineData("https://example.com", "https://example.com:443")]
    [InlineData("HTTPS://Example.COM/", "https://example.com:443")]
    [InlineData("http://example.com", "http://example.com:80")]
    [InlineData("http://example.com:8080", "http://example.com:8080")]
    [InlineData("https://[2606:4700:4700::1111]", "https://[2606:4700:4700::1111]:443")]
    public void Origin_NormalizesSchemeHostAndExplicitPort(string input, string expected)
    {
        Assert.True(ScopeTargetNormalizer.TryNormalize(ScopeType.Origin, input, out var normalized, out var error), error);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("https://user:secret@example.com")]
    [InlineData("https://example.com/path")]
    [InlineData("https://example.com?x=1")]
    [InlineData("https://example.com#fragment")]
    [InlineData("ftp://example.com")]
    [InlineData("not-a-url")]
    public void Origin_RejectsUserInfoPathQueryFragmentAndNonHttp(string input)
    {
        Assert.False(ScopeTargetNormalizer.TryNormalize(ScopeType.Origin, input, out _, out _));
    }

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.1.1")]
    [InlineData("192.168.1.1")]
    [InlineData("172.16.0.1")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("2001:db8::1")]
    public void Classifier_FlagsPrivateAndReserved(string address)
    {
        Assert.True(NetworkAddressClassifier.IsPrivateOrReserved(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("2606:4700:4700::1111")]
    public void Classifier_AllowsPublicUnicast(string address)
    {
        Assert.False(NetworkAddressClassifier.IsPrivateOrReserved(IPAddress.Parse(address)));
    }

    [Fact]
    public void Restrictions_CanonicalizeDeterministicallyAndRejectUnknown()
    {
        Assert.True(ScopeRestrictions.TryCanonicalize(
            [" PRIVATE-NETWORK ", "private-network"],
            out var canonical,
            out var error), error);

        Assert.Equal("private-network", canonical);
        Assert.True(ScopeRestrictions.Has(canonical, ScopeRestrictions.PrivateNetwork));

        Assert.False(ScopeRestrictions.TryCanonicalize(["wildcard"], out _, out var unknown));
        Assert.Contains("Unknown scope restriction", unknown);
    }

    [Fact]
    public void Unmap_ConvertsMappedIPv4()
    {
        var mapped = IPAddress.Parse("::ffff:8.8.8.8");
        Assert.Equal(AddressFamily.InterNetwork, ScopeTargetNormalizer.Unmap(mapped).AddressFamily);
    }
}
