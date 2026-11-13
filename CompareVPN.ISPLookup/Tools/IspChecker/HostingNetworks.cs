namespace CompareVPN.Tools.IspChecker;

/// <summary>
/// Networks that host servers (cloud, datacenter, CDN, VPN exit providers) rather than home or
/// mobile customers, plus networks run by VPN providers themselves. A visitor whose IP belongs to one is very likely using a VPN, proxy or relay.
/// </summary>
/// <remarks>
/// Hand-curated and intentionally conservative: being absent from this list does not mean an
/// IP is residential. Each ASN was checked against the iptoasn.com dataset description.
/// </remarks>
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

    public static bool Contains(int asn) => Asns.Contains(asn);
}
