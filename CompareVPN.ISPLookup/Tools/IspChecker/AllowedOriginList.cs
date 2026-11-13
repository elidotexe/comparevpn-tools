using System.Diagnostics.CodeAnalysis;

namespace CompareVPN.Tools.IspChecker;

/// <summary>Parses and normalises the configured CORS origins.</summary>
public static class AllowedOriginList
{
    private const string WildcardPrefix = "*.";
    private const string Placeholder = "wildcard-subdomain.";
    private static readonly char[] Separators = [',', ';', ' ', '\t', '\n', '\r'];

    /// <summary>
    /// Accepts <c>"https://comparevpn.com"</c>, <c>"https://*.comparevpn.com"</c> (any subdomain),
    /// <c>"http://localhost:3000"</c> or <c>"*"</c> (any site). Entries may be comma-separated.
    /// Origins are normalised the way browsers send them: lowercase, no trailing slash, no default port.
    /// </summary>
    /// <param name="invalidEntry">The first entry that is not a bare origin, when this returns false.</param>
    public static bool TryParse(
        IEnumerable<string?> entries,
        out List<string> origins,
        [NotNullWhen(false)] out string? invalidEntry)
    {
        origins = [];
        invalidEntry = null;

        foreach (var entry in entries.SelectMany(e => (e ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries)))
        {
            if (entry == "*")
            {
                origins.Add(entry);
                continue;
            }

            if (!TryNormalize(entry, out var origin))
            {
                invalidEntry = entry;
                return false;
            }

            origins.Add(origin);
        }

        origins = [.. origins.Distinct(StringComparer.Ordinal)];
        return true;
    }

    private static bool TryNormalize(string entry, [NotNullWhen(true)] out string? origin)
    {
        origin = null;

        // "https://*.example.com" isn't a valid URI, so validate it with a placeholder host label.
        var schemeEnd = entry.IndexOf("://", StringComparison.Ordinal);
        var isWildcard = schemeEnd > 0 && entry.AsSpan(schemeEnd + 3).StartsWith(WildcardPrefix);
        var candidate = isWildcard ? string.Concat(entry.AsSpan(0, schemeEnd + 3), Placeholder, entry.AsSpan(schemeEnd + 5)) : entry;

        // An origin is scheme + host + optional port: no path (other than "/"), query, fragment or credentials.
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || uri.AbsolutePath != "/"
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0
            || uri.UserInfo.Length > 0)
        {
            return false;
        }

        var normalized = uri.GetLeftPart(UriPartial.Authority);
        origin = isWildcard ? normalized.Replace(Placeholder, WildcardPrefix, StringComparison.Ordinal) : normalized;
        return true;
    }
}
