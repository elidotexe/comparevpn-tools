namespace CompareVPN.Tools.IspChecker;

/// <summary>A block of addresses, from <paramref name="Start"/> to <paramref name="End"/>, both included.</summary>
public sealed record IpRange(string Start, string End);
