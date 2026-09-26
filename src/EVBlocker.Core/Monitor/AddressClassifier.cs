using System.Net;
using System.Net.Sockets;

namespace EVBlocker.Core.Monitor;

/// <summary>
/// Decides whether a remote address represents traffic leaving for the internet,
/// as opposed to loopback, LAN, or an address that is never a real destination.
///
/// Pure logic with no Windows dependency, so it is fully unit tested. Every other
/// part of the scan pipeline needs elevation or live sockets; this part does not.
/// </summary>
public static class AddressClassifier
{
    /// <summary>
    /// True when <paramref name="address"/> is a destination outside this machine and its LAN.
    /// </summary>
    public static bool IsInternet(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        // ::ffff:a.b.c.d carries an IPv4 destination, so judge it by that IPv4 value
        // rather than by the IPv6 wrapper.
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsInternetV4(address.GetAddressBytes()),
            AddressFamily.InterNetworkV6 => IsInternetV6(address),
            // Anything else (Unix sockets, IrDA, ...) never leaves for the internet.
            _ => false,
        };
    }

    private static bool IsInternetV4(byte[] b)
    {
        // 0.0.0.0/8 - "this network"; shows up for unconnected sockets, never a destination.
        if (b[0] == 0) return false;

        // 10.0.0.0/8 - private (RFC 1918)
        if (b[0] == 10) return false;

        // 127.0.0.0/8 - loopback
        if (b[0] == 127) return false;

        // 100.64.0.0/10 - shared address space for carrier NAT (RFC 6598).
        // Not a public destination, so it is not counted as internet traffic.
        if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return false;

        // 169.254.0.0/16 - link-local (APIPA), assigned when DHCP fails
        if (b[0] == 169 && b[1] == 254) return false;

        // 172.16.0.0/12 - private (RFC 1918)
        if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false;

        // 192.168.0.0/16 - private (RFC 1918)
        if (b[0] == 192 && b[1] == 168) return false;

        // 224.0.0.0/4 multicast, 240.0.0.0/4 reserved, and 255.255.255.255 broadcast
        // all sit at or above 224 in the first octet.
        if (b[0] >= 224) return false;

        return true;
    }

    private static bool IsInternetV6(IPAddress address)
    {
        // :: - unspecified, the IPv6 equivalent of an unconnected socket
        if (IPAddress.IPv6Any.Equals(address)) return false;

        // ::1 - loopback
        if (IPAddress.IPv6Loopback.Equals(address)) return false;

        // fe80::/10 link-local, ff00::/8 multicast, fc00::/7 unique local (the IPv6 LAN range),
        // fec0::/10 site-local (deprecated but still seen on older kit).
        if (address.IsIPv6LinkLocal) return false;
        if (address.IsIPv6Multicast) return false;
        if (address.IsIPv6UniqueLocal) return false;
        if (address.IsIPv6SiteLocal) return false;

        // Teredo (2001::/32) is a tunnel that carries traffic to the internet, so it counts
        // as internet and is deliberately not excluded here.
        return true;
    }
}
