using IISLogExplorer.Core.Models;
using IISLogExplorer.Core.Networking;

namespace IISLogExplorer.Tests;

public class ClientIpResolverTests
{
    [Theory]
    [InlineData("1.2.3.4, 10.0.0.1", "1.2.3.4")]
    [InlineData("  , 8.8.8.8 ", "8.8.8.8")]
    [InlineData("not-an-ip", null)]
    [InlineData("-", null)]
    public void FirstValidIp_picks_first_valid(string input, string? expected)
    {
        Assert.Equal(expected, ClientIpResolver.FirstValidIp(input));
    }

    [Fact]
    public void Resolve_prefers_custom_header_over_cip()
    {
        var entry = new LogEntry { ClientIp = "10.0.0.1", ForwardedFor = "203.0.113.5" };
        Assert.Equal("203.0.113.5", new ClientIpResolver().Resolve(entry));
    }

    [Fact]
    public void Resolve_falls_back_to_cip()
    {
        var entry = new LogEntry { ClientIp = "10.0.0.1" };
        Assert.Equal("10.0.0.1", new ClientIpResolver().Resolve(entry));
    }
}
