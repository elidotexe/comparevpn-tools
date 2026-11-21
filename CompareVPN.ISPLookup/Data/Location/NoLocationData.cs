using System.Net;

namespace CompareVPN.Data.Location;

/// <summary>Used when location is turned off (an empty <c>LocationDataFilePath</c>): every answer has no location.</summary>
internal sealed class NoLocationData : ILocationDataSource
{
    /// <summary>Always returns <c>null</c>.</summary>
    public ValueTask<IpLocation?> FindAsync(IPAddress address, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IpLocation?>(null);
}
