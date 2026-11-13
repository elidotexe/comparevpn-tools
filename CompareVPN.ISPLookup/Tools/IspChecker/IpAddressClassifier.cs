using System.Net;
using System.Net.Sockets;

namespace CompareVPN.Tools.IspChecker;

/// <summary>
/// This class recognises addresses that never belong to an internet provider's customer: home and office networks,
/// this computer, and blocks set aside for examples, testing or future use.
///
/// In plain terms, here is what happens:
///   1. It picks the list of reserved blocks for the kind of address (IPv4 or IPv6).
///   2. It checks whether the address falls inside any of them. If it does, the address isn't public.
/// </summary>
internal static class IpAddressClassifier
{
    // IPv4 blocks that aren't reachable on the internet: private networks, shared provider space, this computer,
    // self-assigned, documentation examples, benchmarking, multicast and reserved (RFC 1918, 6598, 3927, 5737, 2544, 1112).
    private static readonly IPNetwork[] NonPublicV4 =
    [
        IPNetwork.Parse("0.0.0.0/8"),
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("100.64.0.0/10"),
        IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("169.254.0.0/16"),
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.0.0.0/24"),
        IPNetwork.Parse("192.0.2.0/24"),
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("198.18.0.0/15"),
        IPNetwork.Parse("198.51.100.0/24"),
        IPNetwork.Parse("203.0.113.0/24"),
        IPNetwork.Parse("224.0.0.0/4"),
        IPNetwork.Parse("240.0.0.0/4"),
    ];

    private static readonly IPNetwork[] NonPublicV6 =
    [
        IPNetwork.Parse("::/128"),        // unspecified
        IPNetwork.Parse("::1/128"),       // this computer
        IPNetwork.Parse("fc00::/7"),      // unique local (private networks)
        IPNetwork.Parse("fe80::/10"),     // link-local (self-assigned)
        IPNetwork.Parse("ff00::/8"),      // multicast
        IPNetwork.Parse("2001:db8::/32"), // documentation examples
    ];

    /// <summary>True when <paramref name="address"/> can belong to an internet provider's customer.</summary>
    public static bool IsPublic(IPAddress address)
    {
        // Step 1: Pick the reserved blocks for this kind of address; anything that is neither IPv4 nor IPv6 isn't public.
        var reserved = address.AddressFamily switch
        {
            AddressFamily.InterNetwork => NonPublicV4,
            AddressFamily.InterNetworkV6 => NonPublicV6,
            _ => null,
        };

        // Step 2: Public means outside every reserved block.
        return reserved is not null && !reserved.Any(block => block.Contains(address));
    }
}
