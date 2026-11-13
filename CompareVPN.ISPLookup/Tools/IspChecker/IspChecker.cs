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
///   If the list hasn't been downloaded yet, it says so, so the website can ask the visitor to try again later.
///   The address itself is never stored or written to the logs.
/// </summary>
public sealed class IspChecker(IAsnDataSource dataSource)
{
    /// <summary>
    /// Finds the provider behind <paramref name="address"/>. Returns <see cref="IspCheckOutcome.Checked"/> with a
    /// result whose <c>Network</c> is <c>null</c> for private addresses and addresses the list doesn't know, or
    /// <see cref="IspCheckOutcome.DataNotAvailable"/> when no list is loaded yet.
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

        // Step 4: Describe what was found. "Not in the list" is still an answer from that list, so it keeps its date.
        return search switch
        {
            { Outcome: AsnSearchOutcome.Found, Match: { } match } =>
                IspCheckAnswer.Checked(Describe(tidied, isPublic: true, DescribeNetwork(match), search.DataUpdatedAt)),
            { Outcome: AsnSearchOutcome.NotInDataset } =>
                IspCheckAnswer.Checked(Describe(tidied, isPublic: true, network: null, search.DataUpdatedAt)),
            { Outcome: AsnSearchOutcome.DataNotAvailable } => IspCheckAnswer.DataNotAvailable(),
            _ => throw new InvalidOperationException($"Unexpected search outcome: {search.Outcome}"),
        };
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
}
