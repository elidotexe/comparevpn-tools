using System.Net;

namespace CompareVPN.Tools.IspChecker;

/// <summary>
/// Resolves an IP address to the network that announces it.
/// Implement this to plug in another dataset (IPinfo Lite, MaxMind GeoLite2, ...).
/// </summary>
public interface IAsnDataSource
{
    /// <summary>Returns the announcing network and its block, or null if the address is unrouted or unknown.</summary>
    ValueTask<AsnLookup?> FindAsync(IPAddress address, CancellationToken cancellationToken = default);
}
