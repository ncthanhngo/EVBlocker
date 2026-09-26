using System.Net;
using EVBlocker.Core.Monitor;

namespace EVBlocker.Core.Tests;

public sealed class AddressClassifierTests
{
    [Theory]
    // Ordinary public destinations
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("13.107.42.14")]
    // Boundaries just outside the private ranges - these must NOT be treated as LAN
    [InlineData("9.255.255.255")]
    [InlineData("11.0.0.0")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.0")]
    [InlineData("192.167.255.255")]
    [InlineData("192.169.0.0")]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.0")]
    [InlineData("169.253.255.255")]
    [InlineData("223.255.255.255")]
    public void IsInternet_PublicIPv4_ReturnsTrue(string address)
    {
        Assert.True(AddressClassifier.IsInternet(IPAddress.Parse(address)));
    }

    [Theory]
    // Unspecified / loopback
    [InlineData("0.0.0.0")]
    [InlineData("0.1.2.3")]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.255")]
    // RFC 1918 private
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    // Carrier NAT shared space (RFC 6598)
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.255")]
    // Link-local (APIPA)
    [InlineData("169.254.1.1")]
    // Multicast, reserved, broadcast
    [InlineData("224.0.0.1")]
    [InlineData("239.255.255.255")]
    [InlineData("240.0.0.1")]
    [InlineData("255.255.255.255")]
    public void IsInternet_NonInternetIPv4_ReturnsFalse(string address)
    {
        Assert.False(AddressClassifier.IsInternet(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("2001:4860:4860::8888")]
    [InlineData("2606:4700:4700::1111")]
    // Teredo tunnels traffic out to the internet, so it counts as internet
    [InlineData("2001:0:4136:e378:8000:63bf:3fff:fdd2")]
    public void IsInternet_PublicIPv6_ReturnsTrue(string address)
    {
        Assert.True(AddressClassifier.IsInternet(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("fe80::1")]           // link-local
    [InlineData("fc00::1")]           // unique local
    [InlineData("fd12:3456::1")]      // unique local
    [InlineData("ff02::1")]           // multicast
    public void IsInternet_NonInternetIPv6_ReturnsFalse(string address)
    {
        Assert.False(AddressClassifier.IsInternet(IPAddress.Parse(address)));
    }

    [Theory]
    // An IPv4-mapped address must be judged by the IPv4 value it carries, not by the wrapper.
    [InlineData("::ffff:8.8.8.8", true)]
    [InlineData("::ffff:192.168.1.1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    public void IsInternet_IPv4MappedToIPv6_JudgedByInnerAddress(string address, bool expected)
    {
        Assert.Equal(expected, AddressClassifier.IsInternet(IPAddress.Parse(address)));
    }

    [Fact]
    public void IsInternet_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => AddressClassifier.IsInternet(null!));
    }
}
