using System.Net;

namespace CompareVPN.Data.Location;

/// <summary>
/// Finds roughly where an address is. Self-hosters can implement this to plug in another location list instead of
/// the DB-IP file, or leave location out altogether.
/// </summary>
public interface ILocationDataSource
{
    /// <summary>
    /// Looks <paramref name="address"/> up. Returns <c>null</c> when the address isn't in the list or no list is
    /// loaded: location is an extra, so lookups still answer without it.
    /// </summary>
    ValueTask<IpLocation?> FindAsync(IPAddress address, CancellationToken cancellationToken = default);
}
