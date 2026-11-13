using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;

namespace CompareVPN.Tools.IspChecker;

/// <summary>Parses the configured list of trusted reverse proxies.</summary>
public static class TrustedProxyList
{
    private static readonly char[] Separators = [',', ';', ' ', '\t', '\n', '\r'];

    /// <summary>
    /// Parses entries like <c>"172.18.0.0/16"</c>, <c>"10.0.0.5"</c> or <c>"10.0.0.5, fd00::/8"</c>.
    /// A single address becomes a /32 (IPv4) or /128 (IPv6) network.
    /// </summary>
    /// <param name="invalidEntry">The first entry that could not be parsed, when this returns false.</param>
    public static bool TryParse(
        IEnumerable<string?> entries,
        out List<IPNetwork> networks,
        [NotNullWhen(false)] out string? invalidEntry)
    {
        networks = [];
        invalidEntry = null;

        foreach (var entry in entries.SelectMany(e => (e ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries)))
        {
            if (entry.Contains('/'))
            {
                // Strict: also rejects host bits set ("172.18.0.1/16"), which usually signals a typo.
                // IPNetwork.TryParse accepts that form, so compare the base address explicitly.
                if (!IPNetwork.TryParse(entry, out var network)
                    || !IPAddress.TryParse(entry[..entry.IndexOf('/')], out var baseAddress)
                    || !baseAddress.Equals(network.BaseAddress))
                {
                    invalidEntry = entry;
                    return false;
                }

                networks.Add(network);
            }
            else if (IpAddressInput.TryParse(entry, out var address))
            {
                var normalized = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
                networks.Add(new IPNetwork(normalized, normalized.AddressFamily == AddressFamily.InterNetwork ? 32 : 128));
            }
            else
            {
                invalidEntry = entry;
                return false;
            }
        }

        return true;
    }
}
