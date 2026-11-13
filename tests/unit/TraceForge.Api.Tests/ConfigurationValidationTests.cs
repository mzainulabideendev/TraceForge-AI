namespace TraceForge.Api.Tests;

public sealed class ConfigurationValidationTests
{
    [Theory]
    [InlineData("TraceForge:InstanceName", "", "InstanceName")]
    [InlineData("TraceForge:InstanceName", "   ", "InstanceName")]
    [InlineData("TraceForge:RateLimiting:PermitLimit", "0", "PermitLimit")]
    [InlineData("TraceForge:RateLimiting:PermitLimit", "100001", "PermitLimit")]
    [InlineData("TraceForge:RateLimiting:WindowSeconds", "0", "WindowSeconds")]
    [InlineData("TraceForge:RateLimiting:WindowSeconds", "3601", "WindowSeconds")]
    [InlineData("TraceForge:RateLimiting:QueueLimit", "-1", "QueueLimit")]
    public void InvalidConfiguration_FailsStartupValidation(string key, string value, string expectedMember)
    {
        using var factory = TraceForgeApiFactory.WithConfiguration((key, value));

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(expectedMember, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ValidConfiguration_StartsSuccessfully()
    {
        using var factory = TraceForgeApiFactory.WithConfiguration(
            ("TraceForge:InstanceName", "Validation Instance"),
            ("TraceForge:RateLimiting:PermitLimit", "50"),
            ("TraceForge:RateLimiting:WindowSeconds", "30"));

        using var client = factory.CreateClient();

        Assert.NotNull(client.BaseAddress);
    }
}
