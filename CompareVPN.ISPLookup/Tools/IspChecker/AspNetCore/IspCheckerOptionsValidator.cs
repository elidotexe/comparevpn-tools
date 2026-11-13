using Microsoft.Extensions.Options;

namespace CompareVPN.Tools.IspChecker.AspNetCore;

/// <summary>Fails startup with a clear message instead of silently misbehaving on bad configuration.</summary>
internal sealed class IspCheckerOptionsValidator : IValidateOptions<IspCheckerOptions>
{
    public ValidateOptionsResult Validate(string? name, IspCheckerOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.DataFilePath))
        {
            failures.Add("Tools:IspChecker:DataFilePath must be set.");
        }

        if (!TrustedProxyList.TryParse(options.TrustedProxies, out _, out var invalid))
        {
            failures.Add($"Tools:IspChecker:TrustedProxies contains '{invalid}', which is not an IP address or CIDR block " +
                         "(e.g. 172.18.0.0/16; host bits must be zero).");
        }

        if (!AllowedOriginList.TryParse(options.AllowedOrigins, out _, out var invalidOrigin))
        {
            failures.Add($"Tools:IspChecker:AllowedOrigins contains '{invalidOrigin}', which is not an origin " +
                         "(scheme and host only, e.g. https://comparevpn.com, with no path).");
        }

        if (options.LookupRequestsPerMinute < 1)
        {
            failures.Add("Tools:IspChecker:LookupRequestsPerMinute must be at least 1.");
        }

        if (options.ReloadCheckInterval < TimeSpan.Zero)
        {
            failures.Add("Tools:IspChecker:ReloadCheckInterval cannot be negative (use 00:00:00 to disable).");
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
