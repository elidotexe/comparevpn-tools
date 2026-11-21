namespace CompareVPN.Tools.IspChecker.AspNetCore;

/// <summary>
/// This class holds the ISP checker's settings, read from the <c>Tools:IspChecker</c> section of the configuration
/// (or <c>Tools__IspChecker__&lt;Name&gt;</c> environment variables). Every setting has a safe default, and
/// <see cref="IspCheckerOptionsValidator"/> checks them when the website starts.
/// </summary>
public sealed class IspCheckerOptions
{
    /// <summary>
    /// Where the iptoasn.com file (<c>ip2asn-combined.tsv.gz</c>) is. A relative path starts from the app's folder;
    /// use a full path in production.
    /// </summary>
    public string DataFilePath { get; set; } = "data/ip2asn-combined.tsv.gz";

    /// <summary>
    /// Where the DB-IP "IP to City Lite" file (<c>dbip-city-lite.mmdb</c>) is, for the approximate location in each
    /// answer. A relative path starts from the app's folder. Empty turns location off. When the file isn't there,
    /// lookups still work, just without a location.
    /// </summary>
    public string LocationDataFilePath { get; set; } = "data/dbip-city-lite.mmdb";

    /// <summary>How many typed-address lookups (<c>GET {pattern}/{ip}</c>) each visitor may make per minute.</summary>
    public int LookupRequestsPerMinute { get; set; } = 30;

    /// <summary>
    /// Servers in front of the website (reverse proxies) that may pass on the visitor's address in
    /// <c>X-Forwarded-For</c>, as addresses or blocks such as <c>172.18.0.0/16</c> for a Docker network. Entries may be
    /// comma-separated. Empty means only this computer is trusted; once set, it <em>replaces</em> that default.
    /// </summary>
    public string[] TrustedProxies { get; set; } = [];

    /// <summary>
    /// Websites whose pages may call the API from the browser, e.g. <c>https://comparevpn.co</c>.
    /// <c>https://*.example.com</c> covers every site under example.com and <c>*</c> covers any site. Entries may be
    /// comma-separated. Empty turns this off, so only pages from this website itself can read the answers.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];

    /// <summary>How often to check the file for a newer copy. Zero turns reloading off.</summary>
    public TimeSpan ReloadCheckInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>When the loaded data is older than this, <c>/healthz</c> reports <c>Degraded</c> (still answering 200).</summary>
    public TimeSpan MaxDataAge { get; set; } = TimeSpan.FromDays(3);
}
