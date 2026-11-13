using System.Diagnostics.CodeAnalysis;

namespace CompareVPN.Tools.IspChecker;

/// <summary>
/// This class reads the <c>AllowedOrigins</c> setting: the websites whose pages may call the API from a visitor's
/// browser (for example <c>https://comparevpn.co</c>).
///
/// In plain terms, here is what happens:
///   1. It splits the setting into separate entries (commas, semicolons, spaces and new lines all separate them).
///   2. "*" means any website and is kept as it is.
///   3. Every other entry must be just the start of a web address: "https://", the site name and an optional port,
///      with nothing after it. "https://*.example.com" stands for every site under example.com.
///   4. Entries are written the way browsers send them (lower case, no trailing "/", no default port), and repeats
///      are dropped.
///   It stops at the first entry it can't read and reports it, so the website can refuse to start with a clear
///   message.
/// </summary>
public static class AllowedOriginList
{
    private const string WildcardPrefix = "*.";

    // "https://*.example.com" isn't a valid web address, so the "*." is swapped for this name while checking it.
    private const string Placeholder = "wildcard-subdomain.";

    private static readonly char[] Separators = [',', ';', ' ', '\t', '\n', '\r'];

    /// <summary>
    /// Accepts <c>"https://comparevpn.co"</c>, <c>"https://*.comparevpn.co"</c> (any subdomain),
    /// <c>"http://localhost:3000"</c> or <c>"*"</c> (any site).
    /// </summary>
    /// <param name="entries">The setting's values, each of which may hold several entries.</param>
    /// <param name="origins">The tidied entries, without repeats.</param>
    /// <param name="invalidEntry">The first entry that isn't just the start of a web address, when this returns false.</param>
    public static bool TryParse(
        IEnumerable<string?> entries,
        out List<string> origins,
        [NotNullWhen(false)] out string? invalidEntry)
    {
        origins = [];
        invalidEntry = null;

        // Step 1: One entry at a time.
        foreach (var entry in SplitEntries(entries))
        {
            // Step 2: Any website.
            if (entry == "*")
            {
                origins.Add(entry);
                continue;
            }

            // Step 3 and 4: Check the entry and write it the way browsers send it.
            if (!TryTidy(entry, out var origin))
            {
                invalidEntry = entry;
                return false;
            }

            origins.Add(origin);
        }

        // Step 4: Drop repeats.
        origins = [.. origins.Distinct(StringComparer.Ordinal)];
        return true;
    }

    // ───── Helpers for checking an entry (steps 1, 3 and 4) ─────

    private static IEnumerable<string> SplitEntries(IEnumerable<string?> entries) =>
        entries.SelectMany(entry => (entry ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries));

    private static bool TryTidy(string entry, [NotNullWhen(true)] out string? origin)
    {
        origin = null;
        var isWildcard = IsWildcard(entry, out var schemeEnd);
        var checkable = isWildcard ? WithPlaceholder(entry, schemeEnd) : entry;

        if (!Uri.TryCreate(checkable, UriKind.Absolute, out var address) || !IsJustTheSite(address))
        {
            return false;
        }

        // "Left part" up to the site name and port: drops the trailing "/" and default ports, and writes it in lower case.
        var tidied = address.GetLeftPart(UriPartial.Authority);
        origin = isWildcard ? tidied.Replace(Placeholder, WildcardPrefix, StringComparison.Ordinal) : tidied;
        return true;
    }

    /// <summary>True for "scheme://*.site". <paramref name="schemeEnd"/> is where "://" starts.</summary>
    private static bool IsWildcard(string entry, out int schemeEnd)
    {
        schemeEnd = entry.IndexOf("://", StringComparison.Ordinal);
        return schemeEnd > 0 && entry.AsSpan(schemeEnd + 3).StartsWith(WildcardPrefix);
    }

    /// <summary>"https://*.example.com" becomes "https://wildcard-subdomain.example.com", so it can be checked.</summary>
    private static string WithPlaceholder(string entry, int schemeEnd) =>
        string.Concat(entry.AsSpan(0, schemeEnd + 3), Placeholder, entry.AsSpan(schemeEnd + 3 + WildcardPrefix.Length));

    /// <summary>An http or https address with nothing after the site name and port: no path, query, "#" part or login.</summary>
    private static bool IsJustTheSite(Uri address) =>
        address.Scheme is "http" or "https"
        && address.AbsolutePath == "/"
        && address.Query.Length == 0
        && address.Fragment.Length == 0
        && address.UserInfo.Length == 0;
}
