using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Net;
using CompareVPN.Addresses;

namespace CompareVPN.Web;

/// <summary>
/// This class reads the <c>TrustedProxies</c> setting: the servers in front of the website (such as nginx or
/// Cloudflare) that are allowed to tell it the visitor's real address.
///
/// In plain terms, here is what happens:
///   1. It splits the setting into separate entries (commas, semicolons, spaces and new lines all separate them).
///   2. It reads each entry as either a block of addresses ("172.18.0.0/16") or a single address ("10.0.0.5").
///   3. It stops at the first entry it can't read and reports it, so the website can refuse to start with a clear
///      message instead of quietly trusting the wrong servers.
/// </summary>
public static class TrustedProxyList
{
    private static readonly char[] Separators = [',', ';', ' ', '\t', '\n', '\r'];

    /// <summary>
    /// Reads entries like <c>"172.18.0.0/16"</c>, <c>"10.0.0.5"</c> or <c>"10.0.0.5, fd00::/8"</c>. A single address
    /// becomes a block of one (<c>/32</c> for IPv4, <c>/128</c> for IPv6).
    /// </summary>
    /// <param name="entries">The setting's values, each of which may hold several entries.</param>
    /// <param name="networks">The blocks that were read, in order.</param>
    /// <param name="invalidEntry">The first entry that couldn't be read, when this returns false.</param>
    public static bool TryParse(
        IEnumerable<string?> entries,
        out List<IPNetwork> networks,
        [NotNullWhen(false)] out string? invalidEntry)
    {
        networks = [];
        invalidEntry = null;

        // Step 1: One entry at a time.
        foreach (var entry in SplitEntries(entries))
        {
            // Step 2: A block or a single address.
            if (!TryReadEntry(entry, out var network))
            {
                // Step 3: Report the first entry that doesn't fit.
                invalidEntry = entry;
                return false;
            }

            networks.Add(network);
        }

        return true;
    }

    // ───── Helpers for reading the entries (steps 1–2) ─────

    private static IEnumerable<string> SplitEntries(IEnumerable<string?> entries) =>
        entries.SelectMany(entry => (entry ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries));

    private static bool TryReadEntry(string entry, out IPNetwork network) =>
        entry.Contains('/') ? TryReadBlock(entry, out network) : TryReadSingleAddress(entry, out network);

    /// <summary>
    /// Reads "address/size". Strict on purpose: also rejects a block whose address isn't its first one
    /// ("172.18.0.1/16"), which usually means a typo. .NET accepts that form, so the first address is compared here.
    /// </summary>
    private static bool TryReadBlock(string entry, out IPNetwork network) =>
        IPNetwork.TryParse(entry, out network)
        && IPAddress.TryParse(entry[..entry.IndexOf('/')], out var firstAddress)
        && firstAddress.Equals(network.BaseAddress);

    /// <summary>Reads one address as a block of one. "::ffff:10.0.0.5" counts as the IPv4 address 10.0.0.5.</summary>
    private static bool TryReadSingleAddress(string entry, out IPNetwork network)
    {
        network = default;
        if (!IpAddressInput.TryParse(entry, out var address))
        {
            return false;
        }

        var tidied = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        network = new IPNetwork(tidied, tidied.AddressFamily == AddressFamily.InterNetwork ? 32 : 128);
        return true;
    }
}
