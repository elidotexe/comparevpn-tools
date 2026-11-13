using System.Net;

namespace CompareVPN.Tools.IspChecker;

/// <summary>A match in the list of networks: the network that owns the address, and the block the address is in.</summary>
/// <param name="Network">The network (autonomous system) that announces the block.</param>
/// <param name="RangeStart">First address of the matching block.</param>
/// <param name="RangeEnd">Last address of the matching block.</param>
public sealed record AsnLookup(AsnRecord Network, IPAddress RangeStart, IPAddress RangeEnd);
