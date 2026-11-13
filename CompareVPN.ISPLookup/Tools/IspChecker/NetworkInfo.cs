namespace CompareVPN.Tools.IspChecker;

/// <summary>The provider or network an address belongs to, ready to show to a visitor.</summary>
/// <param name="Asn">Autonomous system number (the network's registered ID), e.g. 7922.</param>
/// <param name="Name">Tidied name people recognise, e.g. "Comcast Xfinity".</param>
/// <param name="Organization">The name exactly as registered, e.g. "COMCAST-7922 - Comcast Cable Communications, LLC".</param>
/// <param name="CountryCode">Two-letter code of the country the network is <em>registered</em> in (not where the visitor is).</param>
/// <param name="Range">The block of addresses the address belongs to.</param>
/// <param name="IsHosting">True when the network is a known datacenter, cloud or VPN company rather than a home internet provider.</param>
public sealed record NetworkInfo(int Asn, string Name, string Organization, string? CountryCode, IpRange Range, bool IsHosting);
