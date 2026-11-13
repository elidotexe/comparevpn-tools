using System.Net;

namespace CompareVPN.Tools.IspChecker;

/// <summary>
/// Finds which network owns an address. Self-hosters can implement this to plug in another list of networks
/// (for example IPinfo Lite or MaxMind GeoLite2) instead of the iptoasn.com file.
/// </summary>
public interface IAsnDataSource
{
    /// <summary>
    /// Looks <paramref name="address"/> up. Returns the matching network, "not in the list", or "data not available"
    /// when nothing is loaded yet.
    /// </summary>
    ValueTask<AsnSearchResult> FindAsync(IPAddress address, CancellationToken cancellationToken = default);
}
