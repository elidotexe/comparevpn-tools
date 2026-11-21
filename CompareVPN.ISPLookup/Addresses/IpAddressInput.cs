using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace CompareVPN.Addresses;

/// <summary>
/// This class reads an address a visitor typed, and only accepts the ordinary ways of writing one.
///
/// In plain terms, here is what happens:
///   1. It trims spaces and turns away empty or overly long text, and addresses with a "%" network-card suffix.
///   2. It asks .NET to read the address.
///   3. For IPv4 it insists on the usual four numbers ("8.8.8.8"). .NET also accepts old shorthands such as "8.8" or
///      "134744072" and silently turns them into a different-looking address, which would confuse visitors.
/// </summary>
public static class IpAddressInput
{
    // The longest an address can be written: an IPv6 address with an IPv4 ending.
    private const int MaxLength = 45;

    /// <summary>
    /// Accepts dotted-quad IPv4 ("8.8.8.8") and standard IPv6 ("2001:4860::8888"). Returns false, with
    /// <paramref name="address"/> set to <c>null</c>, for anything else.
    /// </summary>
    public static bool TryParse(string? input, [NotNullWhen(true)] out IPAddress? address)
    {
        address = null;
        var text = input?.Trim();

        // Step 1: Turn away empty, overly long and "%"-suffixed input.
        if (string.IsNullOrEmpty(text) || text.Length > MaxLength || text.Contains('%'))
        {
            return false;
        }

        // Step 2: Let .NET read it.
        if (!IPAddress.TryParse(text, out var parsed))
        {
            return false;
        }

        // Step 3: Only the usual IPv4 form writes back exactly as typed ("08.8.8.8", "8.8" and "1.2.3" don't).
        if (!text.Contains(':') && parsed.ToString() != text)
        {
            return false;
        }

        address = parsed;
        return true;
    }
}
