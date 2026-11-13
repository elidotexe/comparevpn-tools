using System.Net;
using System.Net.Sockets;

namespace CompareVPN.Tools.IspChecker;

/// <summary>Detects addresses that never belong to an ISP customer (private, loopback, reserved...).</summary>
internal static class IpAddressClassifier
{
    // CIDR blocks that are not publicly routable (RFC 1918, 6598, 3927, 5737, 2544, 1112...).
    private static readonly (IPAddress Network, int PrefixLength)[] NonPublicV4 =
    [
        (IPAddress.Parse("0.0.0.0"), 8),
        (IPAddress.Parse("10.0.0.0"), 8),
        (IPAddress.Parse("100.64.0.0"), 10),
        (IPAddress.Parse("127.0.0.0"), 8),
        (IPAddress.Parse("169.254.0.0"), 16),
        (IPAddress.Parse("172.16.0.0"), 12),
        (IPAddress.Parse("192.0.0.0"), 24),
        (IPAddress.Parse("192.0.2.0"), 24),
        (IPAddress.Parse("192.168.0.0"), 16),
        (IPAddress.Parse("198.18.0.0"), 15),
        (IPAddress.Parse("198.51.100.0"), 24),
        (IPAddress.Parse("203.0.113.0"), 24),
        (IPAddress.Parse("224.0.0.0"), 4),
        (IPAddress.Parse("240.0.0.0"), 4),
    ];

    private static readonly (IPAddress Network, int PrefixLength)[] NonPublicV6 =
    [
        (IPAddress.Parse("::"), 128),
        (IPAddress.Parse("::1"), 128),
        (IPAddress.Parse("fc00::"), 7),     // unique local
        (IPAddress.Parse("fe80::"), 10),    // link-local
        (IPAddress.Parse("ff00::"), 8),     // multicast
        (IPAddress.Parse("2001:db8::"), 32) // documentation
    ];

    public static bool IsPublic(IPAddress address)
    {
        var blocks = address.AddressFamily switch
        {
            AddressFamily.InterNetwork => NonPublicV4,
            AddressFamily.InterNetworkV6 => NonPublicV6,
            _ => null
        };

        if (blocks is null)
        {
            return false;
        }

        foreach (var (network, prefixLength) in blocks)
        {
            if (new IPNetwork(network, prefixLength).Contains(address))
            {
                return false;
            }
        }

        return true;
    }
}
