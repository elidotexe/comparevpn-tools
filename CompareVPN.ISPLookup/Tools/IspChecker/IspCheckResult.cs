namespace CompareVPN.Tools.IspChecker;

/// <summary>What the API sends back about one address. The field names are part of the public API.</summary>
/// <param name="IpAddress">The checked address, tidied (an IPv4 address written in IPv6 form is shown as IPv4).</param>
/// <param name="IpVersion">4 or 6.</param>
/// <param name="IsPublic">False for private, loopback, link-local and other addresses that aren't reachable on the internet.</param>
/// <param name="Network">The network that announces the address, or <c>null</c> when it isn't public or isn't in the list.</param>
/// <param name="DataUpdatedAt">When the list of networks was last refreshed, also when the address wasn't in it; <c>null</c> for addresses that aren't public (no lookup is made).</param>
public sealed record IspCheckResult(
    string IpAddress,
    int IpVersion,
    bool IsPublic,
    NetworkInfo? Network,
    DateTimeOffset? DataUpdatedAt);
