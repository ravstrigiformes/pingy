using System.Net;
using System.Net.Sockets;
using Pingy.Core.Util;

namespace Pingy.Core.Tests;

public class LocalHostTests
{
    [Fact]
    public void Describe_returns_a_non_empty_device_name()
    {
        var info = LocalHost.Describe();
        Assert.False(string.IsNullOrWhiteSpace(info.DeviceName));
    }

    [Fact]
    public void Describe_ipv4_is_null_or_a_parseable_non_loopback_ipv4()
    {
        var info = LocalHost.Describe();
        if (info.IPv4 is null) return; // valid outcome on a host with no usable IPv4

        Assert.True(IPAddress.TryParse(info.IPv4, out var parsed), $"Not a valid IP: {info.IPv4}");
        Assert.Equal(AddressFamily.InterNetwork, parsed!.AddressFamily);
        Assert.False(IPAddress.IsLoopback(parsed));
    }

    [Fact]
    public void Cidr_appends_prefix_when_known_and_falls_back_to_plain_ipv4()
    {
        Assert.Equal("10.0.0.5/24", new LocalHostInfo("PC", "10.0.0.5", 24, null, null).Cidr);
        Assert.Equal("10.0.0.5", new LocalHostInfo("PC", "10.0.0.5", 0, null, null).Cidr);
        Assert.Null(new LocalHostInfo("PC", null, 0, null, null).Cidr);
    }
}
