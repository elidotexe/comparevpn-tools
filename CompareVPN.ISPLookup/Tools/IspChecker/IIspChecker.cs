using System.Net;

namespace CompareVPN.Tools.IspChecker;

/// <summary>Checks which ISP or network an IP address belongs to.</summary>
public interface IIspChecker
{
    /// <summary>Checks <paramref name="address"/>. Never stores or logs the address.</summary>
    ValueTask<IspCheckResult> CheckAsync(IPAddress address, CancellationToken cancellationToken = default);
}
