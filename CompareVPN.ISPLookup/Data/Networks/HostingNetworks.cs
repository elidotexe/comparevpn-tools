namespace CompareVPN.Data.Networks;

/// <summary>
/// This class is a hand-kept list of networks that rent out servers (cloud, datacenter and delivery companies, and the
/// hosts VPN services run on) or belong to VPN services themselves, rather than serving homes and phones. A visitor
/// whose address belongs to one of them is very likely using a VPN, proxy or relay.
///
/// In plain terms, here is what happens:
///   1. A tool (such as the ISP checker) asks whether a network's number is on the list.
///   2. The answer becomes the "isHosting" flag the visitor sees.
///   The list is careful rather than complete: a network that isn't on it is not proof of a home connection.
///   Each entry was checked against the network's name in the iptoasn.com list.
/// </summary>
public static class HostingNetworks
{
    private static readonly HashSet<int> Asns =
    [
        16509,  // AMAZON-02 (AWS)
        14618,  // AMAZON-AES (AWS)
        396982, // GOOGLE-CLOUD-PLATFORM
        8075,   // MICROSOFT-CORP-MSN-AS-BLOCK (Azure)
        31898,  // ORACLE-BMC (Oracle Cloud)
        45102,  // ALIBABA-CN-NET (Alibaba Cloud)
        132203, // TENCENT-NET-AP-CN (Tencent Cloud)
        14061,  // DIGITALOCEAN-ASN
        16276,  // OVH
        24940,  // HETZNER-AS
        63949,  // AKAMAI-LINODE-AP (Linode)
        20473,  // AS-VULTR
        51167,  // CONTABO
        12876,  // Scaleway
        60781,  // LEASEWEB-NL
        28753,  // LEASEWEB-DE
        9009,   // M247 (common VPN exit host)
        60068,  // CDN77 / Datacamp (common VPN exit host)
        212238, // CDNEXT / Datacamp (common VPN exit host)
        62240,  // Clouvider (common VPN exit host)
        136787, // PacketHub (common VPN exit host)
        141039, // PacketHub (common VPN exit host)
        147049, // PacketHub (common VPN exit host)
        207137, // PacketHub (common VPN exit host)
        272096, // PacketHub (common VPN exit host)
        211612, // cache77 / Datacamp (common VPN exit host)
        39351,  // 31173 Services AB (common VPN exit host)
        49399,  // 31173 Services AB (common VPN exit host)
        214813, // 31173 Services AB (common VPN exit host)

        // Consumer VPN providers with their own networks.
        209103, // ProtonVPN (Proton AG)
        199218, // ProtonVPN-2 (Proton AG)
        216025, // MULLVAD-AS (Mullvad VPN AB)
        203619, // IVPN
        397540, // WINDSCRIBE
        46253,  // IPVanish

        13335,  // CLOUDFLARENET (WARP, iCloud Private Relay egress)
        54113,  // FASTLY (iCloud Private Relay egress)
        20940,  // AKAMAI-ASN1
    ];

    /// <summary>True when the network with this number (<paramref name="asn"/>) is on the list.</summary>
    public static bool Contains(int asn) => Asns.Contains(asn);
}
