using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace CompareVPN.Tools.IspChecker;

/// <summary>Parses IP addresses typed by users, rejecting the odd legacy forms IPAddress accepts.</summary>
public static class IpAddressInput
{
    /// <summary>
    /// Accepts dotted-quad IPv4 ("8.8.8.8") and standard IPv6 ("2001:4860::8888").
    /// Rejects shorthand like "8.8" or "134744072" (which <see cref="IPAddress.TryParse(string?, out IPAddress?)"/>
    /// would silently turn into another address), zone IDs, and surrounding junk.
    /// </summary>
    public static bool TryParse(string? input, [NotNullWhen(true)] out IPAddress? address)
    {
        address = null;
        var text = input?.Trim();

        if (string.IsNullOrEmpty(text) || text.Length > 45 || text.Contains('%'))
        {
            return false;
        }

        if (!IPAddress.TryParse(text, out var parsed))
        {
            return false;
        }

        // For IPv4, only the canonical dotted-quad form round-trips ("08.8.8.8", "8.8", "1.2.3" don't).
        if (!text.Contains(':') && parsed.ToString() != text)
        {
            return false;
        }

        address = parsed;
        return true;
    }
}
