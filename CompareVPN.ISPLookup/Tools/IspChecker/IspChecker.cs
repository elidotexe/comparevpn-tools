using System.Net;
using System.Net.Sockets;

namespace CompareVPN.Tools.IspChecker;

/// <summary>
/// This class answers "which internet provider is behind this address?", like looking a street address up in a
/// directory to find out who owns the building.
///
/// In plain terms, here is what happens:
///   1. It tidies the address, so two ways of writing the same address are treated alike.
///   2. It checks whether the address is private (a home network or this computer). Those have no provider, so it
///      answers straight away without looking anything up.
///   3. It looks the address up in the downloaded list of networks.
///   4. It turns the network's registered name into the name people know, and notes whether the network belongs to
///      a hosting or VPN company rather than a home internet provider.
///   5. If a location list is available, it adds roughly where the address is (city, region, country), with the
///      map position rounded so it never looks more exact than it is.
///   If the list of networks hasn't been downloaded yet, it says so, so the website can ask the visitor to try again
///   later. A missing location list only leaves the location out. The address itself is never stored or written to
///   the logs.
/// </summary>
/// <param name="dataSource">The list of networks.</param>
/// <param name="locationSource">The location list; without one, answers have no location.</param>
public sealed class IspChecker(IAsnDataSource dataSource, ILocationDataSource? locationSource = null)
{
    // Two decimals of latitude or longitude is about a kilometre: as close as a city-level estimate deserves.
    private const int CoordinateDecimals = 2;

    /// <summary>
    /// Finds the provider behind <paramref name="address"/>, and roughly where it is. Returns
    /// <see cref="IspCheckOutcome.Checked"/> with a result whose <c>Network</c> and <c>Location</c> are <c>null</c>
    /// for private addresses and addresses the lists don't know, or <see cref="IspCheckOutcome.DataNotAvailable"/> when
    /// no list of networks is loaded yet.
    /// </summary>
    public async ValueTask<IspCheckAnswer> CheckAsync(IPAddress address, CancellationToken cancellationToken = default)
    {
        // Step 1: Treat "::ffff:1.1.1.1" (an IPv4 address written in IPv6 form) the same as "1.1.1.1".
        var tidied = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        // Step 2: Private addresses have no provider to look up.
        if (!IpAddressClassifier.IsPublic(tidied))
        {
            return IspCheckAnswer.Checked(Describe(tidied, isPublic: false, network: null, dataUpdatedAt: null));
        }

        // Step 3: Look the address up in the list of networks.
        var search = await dataSource.FindAsync(tidied, cancellationToken);
        if (search.Outcome == AsnSearchOutcome.DataNotAvailable)
        {
            return IspCheckAnswer.DataNotAvailable();
        }

        // Step 4: Describe what was found. "Not in the list" is still an answer from that list, so it keeps its date.
        var network = search switch
        {
            { Outcome: AsnSearchOutcome.Found, Match: { } match } => DescribeNetwork(match),
            { Outcome: AsnSearchOutcome.NotInDataset } => null,
            _ => throw new InvalidOperationException($"Unexpected search outcome: {search.Outcome}"),
        };

        // Step 5: Add roughly where the address is.
        var location = await FindLocationAsync(tidied, cancellationToken);
        return IspCheckAnswer.Checked(Describe(tidied, isPublic: true, network, search.DataUpdatedAt) with { Location = location });
    }

    // ───── Helpers for describing the answer (steps 2 and 4) ─────

    private static IspCheckResult Describe(IPAddress address, bool isPublic, NetworkInfo? network, DateTimeOffset? dataUpdatedAt)
    {
        var ipVersion = address.AddressFamily == AddressFamily.InterNetworkV6 ? 6 : 4;
        return new IspCheckResult(address.ToString(), ipVersion, isPublic, network, dataUpdatedAt);
    }

    /// <summary>Turns a match from the list into what visitors see: a recognisable name and a hosting flag.</summary>
    private static NetworkInfo DescribeNetwork(AsnLookup match) =>
        new(
            match.Network.Asn,
            IspNameFormatter.Format(match.Network.Organization),
            match.Network.Organization,
            match.Network.CountryCode,
            new IpRange(match.RangeStart.ToString(), match.RangeEnd.ToString()),
            HostingNetworks.Contains(match.Network.Asn));

    // ───── Helpers for the location (step 5) ─────

    /// <summary>Looks the address up in the location list, if there is one, and rounds the map position.</summary>
    private async ValueTask<IpLocation?> FindLocationAsync(IPAddress address, CancellationToken cancellationToken)
    {
        if (locationSource is null || await locationSource.FindAsync(address, cancellationToken) is not { } location)
        {
            return null;
        }

        return location with { Latitude = Round(location.Latitude), Longitude = Round(location.Longitude) };
    }

    private static double? Round(double? coordinate) =>
        coordinate is { } value ? Math.Round(value, CoordinateDecimals, MidpointRounding.AwayFromZero) : null;
}
