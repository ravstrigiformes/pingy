using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Pingy.Core.Util;

/// <summary>
/// Identity and LAN attachment of the machine a Pingy process runs on: device name,
/// primary IPv4, its subnet prefix, the default gateway, and the adapter name.
/// </summary>
public sealed record LocalHostInfo(
    string DeviceName,
    string? IPv4,
    int PrefixLength,
    string? Gateway,
    string? AdapterName)
{
    /// <summary>IPv4 with its CIDR suffix (e.g. "192.168.17.217/24"); plain IPv4 when the
    /// prefix is unknown; <c>null</c> when the host has no usable IPv4.</summary>
    public string? Cidr =>
        IPv4 is null ? null
        : PrefixLength is > 0 and <= 32 ? $"{IPv4}/{PrefixLength}"
        : IPv4;
}

/// <summary>
/// Reads the identity of the local machine. Air-gap safe — enumerates local NIC
/// configuration only; sends no traffic and does no internet DNS. The Agent will
/// reuse this when stamping HTTPS uploads with their origin.
/// </summary>
public static class LocalHost
{
    /// <summary>Windows computer name (e.g. "DESKTOP-A1B2C3").</summary>
    public static string DeviceName => Environment.MachineName;

    /// <summary>
    /// Best-effort snapshot of the machine's identity and LAN attachment. Prefers the
    /// interface that has a default gateway (the real LAN NIC) over virtual, host-only
    /// or VPN adapters. IPv4-related fields are <c>null</c> when no usable IPv4 exists.
    /// </summary>
    public static LocalHostInfo Describe()
    {
        try
        {
            var live = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up
                          && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback
                          && ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                // A NIC with a gateway is the one carrying real LAN traffic — sort it first.
                .OrderByDescending(ni => ni.GetIPProperties().GatewayAddresses
                    .Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork));

            foreach (var ni in live)
            {
                var props = ni.GetIPProperties();
                var ipv4 = props.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork
                                      && !IPAddress.IsLoopback(a.Address));
                if (ipv4 is null) continue;

                var gateway = props.GatewayAddresses
                    .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork
                                      && !g.Address.Equals(IPAddress.Any))
                    ?.Address.ToString();

                return new LocalHostInfo(
                    DeviceName,
                    ipv4.Address.ToString(),
                    ipv4.PrefixLength,
                    gateway,
                    ni.Name);
            }
        }
        catch
        {
            // NIC enumeration can throw on locked-down hosts — fall back to DNS below.
        }

        string? dnsIp = null;
        try
        {
            dnsIp = Dns.GetHostAddresses(Dns.GetHostName())
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork
                                  && !IPAddress.IsLoopback(a))
                ?.ToString();
        }
        catch
        {
            // Leave dnsIp null — caller renders a "no IPv4" placeholder.
        }

        return new LocalHostInfo(DeviceName, dnsIp, 0, null, null);
    }
}
