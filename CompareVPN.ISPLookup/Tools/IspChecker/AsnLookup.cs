using System.Net;

namespace CompareVPN.Tools.IspChecker;

/// <summary>A dataset match: the announcing network plus the address block the IP falls in.</summary>
/// <param name="Network">The network (autonomous system) that announces the block.</param>
/// <param name="RangeStart">First address of the matching block.</param>
/// <param name="RangeEnd">Last address of the matching block.</param>
/// <param name="DataUpdatedAt">When the dataset was last refreshed, if the source knows.</param>
public sealed record AsnLookup(AsnRecord Network, IPAddress RangeStart, IPAddress RangeEnd, DateTimeOffset? DataUpdatedAt);
