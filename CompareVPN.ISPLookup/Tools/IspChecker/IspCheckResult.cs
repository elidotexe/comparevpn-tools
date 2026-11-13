namespace CompareVPN.Tools.IspChecker;

/// <summary>The outcome of checking a single IP address.</summary>
/// <param name="IpAddress">The checked address, normalised (IPv4-mapped IPv6 is shown as IPv4).</param>
/// <param name="IpVersion">4 or 6.</param>
/// <param name="IsPublic">False for private, loopback, link-local and other non-routable addresses.</param>
/// <param name="Network">The network that announces the address, or null when it is not public or not found.</param>
/// <param name="DataUpdatedAt">When the ISP dataset was last refreshed, also when the address was not found in it; null for non-public addresses (no lookup is made).</param>
public sealed record IspCheckResult(
    string IpAddress,
    int IpVersion,
    bool IsPublic,
    NetworkInfo? Network,
    DateTimeOffset? DataUpdatedAt);

/// <summary>The ISP / network an address belongs to, ready for display.</summary>
/// <param name="Asn">Autonomous system number, e.g. 7922.</param>
/// <param name="Name">Cleaned, human-friendly operator name, e.g. "Comcast Xfinity".</param>
/// <param name="Organization">Raw operator description from the dataset, e.g. "COMCAST-7922 - Comcast Cable Communications, LLC".</param>
/// <param name="CountryCode">ISO 3166-1 alpha-2 code of the country the network is <em>registered</em> in (not the visitor's location).</param>
/// <param name="Range">The address block the IP belongs to.</param>
/// <param name="IsHosting">True when the network is a known datacenter / cloud / VPN-exit operator rather than a consumer ISP.</param>
public sealed record NetworkInfo(int Asn, string Name, string Organization, string? CountryCode, IpRange Range, bool IsHosting);

/// <summary>An inclusive range of IP addresses.</summary>
public sealed record IpRange(string Start, string End);
