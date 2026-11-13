namespace CompareVPN.Tools.IspChecker;

/// <summary>
/// A data source's answer to "who owns this address?": what happened, the matching block of addresses when there
/// is one, and how fresh the list of networks is.
/// </summary>
/// <param name="Outcome">Whether the address was found, not found, or couldn't be looked up.</param>
/// <param name="Match">The network and block the address belongs to; only set when <paramref name="Outcome"/> is <see cref="AsnSearchOutcome.Found"/>.</param>
/// <param name="DataUpdatedAt">When the list was last refreshed, if the source knows; <c>null</c> when no list is loaded.</param>
public sealed record AsnSearchResult(AsnSearchOutcome Outcome, AsnLookup? Match, DateTimeOffset? DataUpdatedAt)
{
    /// <summary>The address belongs to <paramref name="match"/>.</summary>
    public static AsnSearchResult Found(AsnLookup match, DateTimeOffset? dataUpdatedAt) =>
        new(AsnSearchOutcome.Found, match, dataUpdatedAt);

    /// <summary>The list is loaded but doesn't contain the address.</summary>
    public static AsnSearchResult NotInDataset(DateTimeOffset? dataUpdatedAt) =>
        new(AsnSearchOutcome.NotInDataset, null, dataUpdatedAt);

    /// <summary>No list is loaded, so nothing could be looked up.</summary>
    public static AsnSearchResult DataNotAvailable() => new(AsnSearchOutcome.DataNotAvailable, null, null);
}
