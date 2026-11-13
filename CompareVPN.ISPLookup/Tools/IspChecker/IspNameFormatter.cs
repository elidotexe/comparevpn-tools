using System.Globalization;
using System.Text.RegularExpressions;

namespace CompareVPN.Tools.IspChecker;

/// <summary>
/// Turns raw dataset descriptions into names people recognise, e.g.
/// "COMCAST-7922 - Comcast Cable Communications, LLC" → "Comcast Xfinity" and
/// "DTAG Deutsche Telekom AG" → "Deutsche Telekom".
/// </summary>
/// <remarks>
/// The iptoasn.com descriptions come in two shapes: "HANDLE - Name" (ARIN/APNIC style) and
/// "HANDLE Name" (RIPE style). This is a best-effort heuristic; the raw description is always
/// returned alongside so nothing is lost when it guesses wrong.
/// </remarks>
public static partial class IspNameFormatter
{
    // Consumer brands for networks whose registered name differs from what customers know.
    // Keyed by the cleaned name, so it covers every ASN of that operator (Charter has dozens).
    private static readonly Dictionary<string, string> Brands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Comcast Cable Communications"] = "Comcast Xfinity",
        ["Charter Communications"] = "Spectrum (Charter)",
        ["AT&T Enterprises"] = "AT&T",
        ["Verizon Business"] = "Verizon",
        ["Cox Communications"] = "Cox",
        ["T-Mobile USA"] = "T-Mobile",
        ["British Telecommunications"] = "BT",
        ["Rogers Communications Canada"] = "Rogers",
        ["The Constant Company"] = "Vultr",
    };

    public static string Format(string organization)
    {
        ArgumentNullException.ThrowIfNull(organization);

        var name = StripHandle(organization.Trim());
        name = StripLegalSuffixes(name);
        name = SoftenAllCaps(name);

        if (name.Length == 0)
        {
            return organization.Trim();
        }

        return Brands.TryGetValue(name, out var brand) ? brand : name;
    }

    private static string StripHandle(string text)
    {
        // "HANDLE - Name" (the handle is a single token; a " - " later on is part of the name)
        var separator = text.IndexOf(" - ", StringComparison.Ordinal);
        if (separator > 0 && !text[..separator].Contains(' '))
        {
            var rest = text[(separator + 3)..].Trim();
            return rest.Length > 0 ? rest : text;
        }

        // "HANDLE Name": drop the first word when it is a registry handle followed by a real name.
        var space = text.IndexOf(' ');
        if (space <= 0)
        {
            return text;
        }

        var first = text[..space];
        var remainder = text[(space + 1)..].TrimStart();
        var nextWord = remainder.Split(' ', 2)[0];

        // "DTAG Deutsche Telekom": an all-caps handle before a mixed-case name.
        var isCapsHandle = !first.Any(char.IsLower) && remainder.Any(char.IsLower);

        // "OVH OVH SAS", "netplans-cloud NetPlans", "Emdadkhodro-Iran-AS Emdadkhodro Iran", "ProtonVPN Proton AG":
        // the handle (or its first hyphen-separated part) repeats or starts with the next word.
        var handleStem = first.Split('-', 2)[0];
        var isRepeat = string.Equals(first, nextWord, StringComparison.OrdinalIgnoreCase)
            || (first.Contains('-') && string.Equals(handleStem, nextWord, StringComparison.OrdinalIgnoreCase))
            || (nextWord.Length >= 3 && first.StartsWith(nextWord, StringComparison.OrdinalIgnoreCase));

        // "serviceplan-as Serviceplan Group": a hyphenated "-AS" handle in any case.
        var isAsHandle = first.Contains('-') && first.EndsWith("-as", StringComparison.OrdinalIgnoreCase);

        return remainder.Length > 0 && (isCapsHandle || isRepeat || isAsHandle) ? remainder : text;
    }

    private static string SoftenAllCaps(string name)
    {
        // "BELL MOBILITY" → "Bell Mobility". Short words ("PT", "TDM") and words with digits are
        // usually acronyms, so they stay as they are, as do single-word names ("BSITC") and mixed-case names.
        if (name.Any(char.IsLower) || !name.Any(char.IsUpper) || !name.Contains(' '))
        {
            return name;
        }

        var textInfo = CultureInfo.InvariantCulture.TextInfo;
        return string.Join(' ', name.Split(' ').Select(word =>
            word.Length > 3 && word.All(c => char.IsLetter(c) || c is '-' or '&' or '.' or '\'')
                ? textInfo.ToTitleCase(word.ToLowerInvariant())
                : word));
    }

    private static string StripLegalSuffixes(string name)
    {
        // Repeat so "Suzuka Cable Co.,Ltd" loses both "Ltd" and "Co".
        string previous;
        do
        {
            previous = name;
            name = LegalSuffix().Replace(name, string.Empty).TrimEnd(' ', ',', '.', '&');
        }
        while (name != previous && name.Length > 0);

        return name.Length > 0 ? name : previous;
    }

    // A trailing legal-form token, preceded by at least one character of real name.
    [GeneratedRegex(
        @"(?<=\S)[\s,.&]+(inc|incorporated|llc|l\.l\.c|ltd|limited|gmbh|ag|sas|s\.a\.s|s\.a|sa|b\.v|bv|srl|s\.r\.l|" +
        @"co|corp|corporation|plc|pty|pte|llp|l\.p|lp|sdn\.?\s?bhd|bhd|private|d\.o\.o|oy|ab|nv|n\.v|spa|s\.p\.a|kft|sro|s\.r\.o|ltda|sarl|s\.l|sl|kg|se|c\.a|sal|eireli|s\.a\.c)\.?,?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LegalSuffix();
}
