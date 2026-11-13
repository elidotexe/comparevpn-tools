namespace CompareVPN.Tools.IspChecker.AspNetCore;

/// <summary>Options bound from configuration (e.g. the <c>Tools:IspChecker</c> section).</summary>
public sealed class IspCheckerOptions
{
    /// <summary>
    /// Path to the iptoasn.com dataset (<c>ip2asn-combined.tsv.gz</c>).
    /// Relative paths are resolved against the app's content root; use an absolute path in production
    /// (e.g. via the <c>Tools__IspChecker__DataFilePath</c> environment variable).
    /// </summary>
    public string DataFilePath { get; set; } = "data/ip2asn-combined.tsv.gz";

    /// <summary>Per-client limit for looking up arbitrary IPs (<c>GET {pattern}/{ip}</c>).</summary>
    public int LookupRequestsPerMinute { get; set; } = 30;

    /// <summary>
    /// Reverse proxies allowed to set <c>X-Forwarded-For</c>, as IPs or CIDR blocks
    /// (e.g. <c>172.18.0.0/16</c> for a Docker network). Entries may also be comma-separated.
    /// When empty, only loopback is trusted. When set, it <em>replaces</em> the loopback default.
    /// </summary>
    public string[] TrustedProxies { get; set; } = [];

    /// <summary>
    /// Websites whose browser JavaScript may call the API, e.g. <c>https://comparevpn.com</c>.
    /// Supports <c>https://*.example.com</c> for subdomains and <c>*</c> for any site. Entries may also be
    /// comma-separated. When empty, CORS is off and only same-origin pages can read responses.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];

    /// <summary>How often to check the dataset file for changes and reload it. Zero disables reloading.</summary>
    public TimeSpan ReloadCheckInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Data older than this makes <c>/healthz</c> report <c>Degraded</c> (still HTTP 200).</summary>
    public TimeSpan MaxDataAge { get; set; } = TimeSpan.FromDays(3);
}
