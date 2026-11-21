using CompareVPN.Web;
using Microsoft.Extensions.Options;

namespace CompareVPN.Tools.IspChecker.AspNetCore;

/// <summary>
/// This class checks the ISP checker's settings when the website starts, so a mistake stops start-up with a clear
/// message instead of making the website quietly misbehave later.
///
/// In plain terms, here is what happens:
///   1. It checks each setting in turn and writes down a message for every one that is wrong, naming the bad entry.
///   2. If anything was written down, start-up stops and all the messages are shown together.
/// </summary>
internal sealed class IspCheckerOptionsValidator : IValidateOptions<IspCheckerOptions>
{
    /// <summary>Returns success, or a failure listing every setting that is wrong.</summary>
    public ValidateOptionsResult Validate(string? name, IspCheckerOptions options)
    {
        // Step 1: Collect a message for every setting that is wrong.
        string?[] problems =
        [
            CheckDataFilePath(options),
            CheckTrustedProxies(options),
            CheckAllowedOrigins(options),
            CheckLookupLimit(options),
            CheckReloadInterval(options),
        ];
        var failures = problems.OfType<string>().ToList();

        // Step 2: Any message stops start-up.
        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }

    // ───── One check per setting (step 1); each returns a message, or null when the setting is fine ─────

    private static string? CheckDataFilePath(IspCheckerOptions options) =>
        string.IsNullOrWhiteSpace(options.DataFilePath) ? "Tools:IspChecker:DataFilePath must be set." : null;

    private static string? CheckTrustedProxies(IspCheckerOptions options) =>
        TrustedProxyList.TryParse(options.TrustedProxies, out _, out var invalid)
            ? null
            : $"Tools:IspChecker:TrustedProxies contains '{invalid}', which is not an IP address or CIDR block " +
              "(e.g. 172.18.0.0/16; host bits must be zero).";

    private static string? CheckAllowedOrigins(IspCheckerOptions options) =>
        AllowedOriginList.TryParse(options.AllowedOrigins, out _, out var invalid)
            ? null
            : $"Tools:IspChecker:AllowedOrigins contains '{invalid}', which is not an origin " +
              "(scheme and host only, e.g. https://comparevpn.co, with no path).";

    private static string? CheckLookupLimit(IspCheckerOptions options) =>
        options.LookupRequestsPerMinute < 1 ? "Tools:IspChecker:LookupRequestsPerMinute must be at least 1." : null;

    private static string? CheckReloadInterval(IspCheckerOptions options) =>
        options.ReloadCheckInterval < TimeSpan.Zero
            ? "Tools:IspChecker:ReloadCheckInterval cannot be negative (use 00:00:00 to disable)."
            : null;
}
