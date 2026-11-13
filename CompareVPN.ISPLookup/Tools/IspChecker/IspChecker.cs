using System.Net;
using System.Net.Sockets;

namespace CompareVPN.Tools.IspChecker;

/// <inheritdoc />
public sealed class IspChecker(IAsnDataSource dataSource) : IIspChecker
{
    /// <inheritdoc />
    public async ValueTask<IspCheckResult> CheckAsync(IPAddress address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);

        var normalized = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        var ipVersion = normalized.AddressFamily == AddressFamily.InterNetworkV6 ? 6 : 4;

        if (!IpAddressClassifier.IsPublic(normalized))
        {
            return new IspCheckResult(normalized.ToString(), ipVersion, IsPublic: false, Network: null, DataUpdatedAt: null);
        }

        var lookup = await dataSource.FindAsync(normalized, cancellationToken);
        var network = lookup is null
            ? null
            : new NetworkInfo(
                lookup.Network.Asn,
                IspNameFormatter.Format(lookup.Network.Organization),
                lookup.Network.Organization,
                lookup.Network.CountryCode,
                new IpRange(lookup.RangeStart.ToString(), lookup.RangeEnd.ToString()),
                HostingNetworks.Contains(lookup.Network.Asn));

        // "Not in the dataset" is still an answer from that dataset, so report its date either way.
        var dataUpdatedAt = lookup?.DataUpdatedAt ?? (dataSource as IReloadableAsnDataSource)?.DataUpdatedAt;

        return new IspCheckResult(normalized.ToString(), ipVersion, IsPublic: true, network, dataUpdatedAt);
    }
}
