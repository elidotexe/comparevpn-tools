using System.Globalization;
using System.Text.RegularExpressions;

namespace CompareVPN.Tools.IspChecker;

/// <summary>
/// This class turns the names networks are registered under into names people recognise, for example
/// "COMCAST-7922 - Comcast Cable Communications, LLC" into "Comcast Xfinity", and "DTAG Deutsche Telekom AG" into
/// "Deutsche Telekom".
///
/// In plain terms, here is what happens:
///   1. It removes the registry's short code in front of the name. Names come in two shapes: "CODE - Name" and
///      "CODE Name".
///   2. It removes legal endings such as "Inc", "LLC", "GmbH" or "Co., Ltd".
///   3. It softens names written all in capitals ("BELL MOBILITY" becomes "Bell Mobility"), leaving short words and
///      codes alone.
///   4. It swaps in the brand customers know, for networks whose registered name is different ("Charter
///      Communications" is sold as Spectrum).
///   This is a best guess. The registered name is always sent alongside, so nothing is lost when it guesses wrong.
/// </summary>
public static partial class IspNameFormatter
{
    // Brands for networks whose registered name differs from what customers know. Looked up by the tidied name, so
    // one entry covers every network of that company (Charter has dozens).
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

    /// <summary>
    /// Returns a recognisable name for a registered network name (<paramref name="organization"/>). Falls back to the
    /// registered name, trimmed, if tidying would leave nothing.
    /// </summary>
    public static string Format(string organization)
    {
        // Step 1: Remove the registry code.
        var name = StripRegistryCode(organization.Trim());

        // Step 2: Remove legal endings.
        name = StripLegalEndings(name);

        // Step 3: Soften all-capitals names.
        name = SoftenAllCaps(name);

        if (name.Length == 0)
        {
            return organization.Trim();
        }

        // Step 4: Use the brand customers know, when there is one.
        return Brands.TryGetValue(name, out var brand) ? brand : name;
    }

    // ───── Helpers for removing the registry code (step 1) ─────

    private static string StripRegistryCode(string text) =>
        TryStripCodeBeforeDash(text, out var name) ? name : StripCodeWord(text);

    /// <summary>
    /// "CODE - Name": returns true when the text has this shape, with <paramref name="name"/> set to what follows the
    /// dash. The code is a single word, so a " - " later in the text is part of the name.
    /// </summary>
    private static bool TryStripCodeBeforeDash(string text, out string name)
    {
        name = text;
        var separator = text.IndexOf(" - ", StringComparison.Ordinal);
        if (separator <= 0 || text[..separator].Contains(' '))
        {
            return false;
        }

        var rest = text[(separator + 3)..].Trim();
        name = rest.Length > 0 ? rest : text;
        return true;
    }

    /// <summary>"CODE Name": drops the first word when it looks like a registry code followed by a real name.</summary>
    private static string StripCodeWord(string text)
    {
        var space = text.IndexOf(' ');
        if (space <= 0)
        {
            return text;
        }

        var firstWord = text[..space];
        var rest = text[(space + 1)..].TrimStart();
        return rest.Length > 0 && LooksLikeRegistryCode(firstWord, rest) ? rest : text;
    }

    private static bool LooksLikeRegistryCode(string firstWord, string rest)
    {
        var nextWord = rest.Split(' ', 2)[0];

        // "DTAG Deutsche Telekom": a code in capitals before a name in mixed case.
        var isCapitalsCode = !firstWord.Any(char.IsLower) && rest.Any(char.IsLower);

        // "OVH OVH SAS", "netplans-cloud NetPlans", "Emdadkhodro-Iran-AS Emdadkhodro Iran", "ProtonVPN Proton AG":
        // the code (or its part before the first hyphen) repeats or starts with the next word.
        var codeStem = firstWord.Split('-', 2)[0];
        var isRepeat = string.Equals(firstWord, nextWord, StringComparison.OrdinalIgnoreCase)
            || (firstWord.Contains('-') && string.Equals(codeStem, nextWord, StringComparison.OrdinalIgnoreCase))
            || (nextWord.Length >= 3 && firstWord.StartsWith(nextWord, StringComparison.OrdinalIgnoreCase));

        // "serviceplan-as Serviceplan Group": a hyphenated code ending in "-AS", in any case.
        var isAsCode = firstWord.Contains('-') && firstWord.EndsWith("-as", StringComparison.OrdinalIgnoreCase);

        return isCapitalsCode || isRepeat || isAsCode;
    }

    // ───── Helpers for legal endings and capitals (steps 2–3) ─────

    private static string StripLegalEndings(string name)
    {
        // Repeat, so "Suzuka Cable Co.,Ltd" loses both "Ltd" and "Co".
        string previous;
        do
        {
            previous = name;
            name = LegalEnding().Replace(name, string.Empty).TrimEnd(' ', ',', '.', '&');
        }
        while (name != previous && name.Length > 0);

        // Never strip a name down to nothing: "Ltd" alone stays "Ltd".
        return name.Length > 0 ? name : previous;
    }

    private static string SoftenAllCaps(string name)
    {
        // Only names of several words written entirely in capitals. Single words ("BSITC") are usually codes.
        if (name.Any(char.IsLower) || !name.Any(char.IsUpper) || !name.Contains(' '))
        {
            return name;
        }

        // Words of up to three letters ("PT", "TDM") and words with digits are usually abbreviations, so they stay.
        var textInfo = CultureInfo.InvariantCulture.TextInfo;
        return string.Join(' ', name.Split(' ').Select(word =>
            word.Length > 3 && word.All(c => char.IsLetter(c) || c is '-' or '&' or '.' or '\'')
                ? textInfo.ToTitleCase(word.ToLowerInvariant())
                : word));
    }

    // A legal ending at the very end, after at least one character of real name.
    [GeneratedRegex(
        @"(?<=\S)[\s,.&]+(inc|incorporated|llc|l\.l\.c|ltd|limited|gmbh|ag|sas|s\.a\.s|s\.a|sa|b\.v|bv|srl|s\.r\.l|" +
        @"co|corp|corporation|plc|pty|pte|llp|l\.p|lp|sdn\.?\s?bhd|bhd|private|d\.o\.o|oy|ab|nv|n\.v|spa|s\.p\.a|kft|sro|s\.r\.o|ltda|sarl|s\.l|sl|kg|se|c\.a|sal|eireli|s\.a\.c)\.?,?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LegalEnding();
}
