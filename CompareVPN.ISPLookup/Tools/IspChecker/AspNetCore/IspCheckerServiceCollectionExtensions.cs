using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CompareVPN.Tools.IspChecker.AspNetCore;

/// <summary>
/// This class sets up the ISP checker when the website starts: it is the list of parts the checker needs, and how each
/// part is configured from the settings.
///
/// In plain terms, here is what happens:
///   1. It reads the <c>Tools:IspChecker</c> settings and has them checked at start-up.
///   2. It adds the list of networks (read from the file in <c>DataFilePath</c>), the optional location list (from
///      <c>LocationDataFilePath</c>) and the checker that uses them.
///   3. It tells the website which servers in front of it may pass on the visitor's real address.
///   4. It lets the websites in <c>AllowedOrigins</c> call the API from a visitor's browser.
///   5. It limits how many typed-address lookups each visitor can make per minute.
///   6. It adds the background job that loads and reloads the files, and the <c>/healthz</c> check.
/// </summary>
public static class IspCheckerServiceCollectionExtensions
{
    /// <summary>
    /// Adds the ISP checker and everything it needs, with settings from <paramref name="configuration"/> (the
    /// <c>Tools:IspChecker</c> section). The app still has to call <c>UseForwardedHeaders()</c>, <c>UseCors()</c> and
    /// <c>UseRateLimiter()</c>, and <c>MapIspChecker()</c> for the web addresses.
    /// </summary>
    public static IServiceCollection AddIspChecker(this IServiceCollection services, IConfiguration configuration)
    {
        // Step 1: Settings.
        services.AddValidatedOptions(configuration);

        // Step 2: The list of networks, the location list and the checker.
        services.AddLookup();

        // Step 3: Trusted proxies.
        services.AddTrustedProxies();

        // Step 4: Which websites may call the API.
        services.AddCorsPolicy();

        // Step 5: The lookup limit.
        services.AddRateLimiting();

        // Step 6: Loading the files in the background, and the health check.
        services.AddHostedService<IspCheckerDatasetService>();
        services.AddHealthChecks().AddCheck<IspCheckerHealthCheck>("isp-dataset");

        return services;
    }

    // ───── Helpers for each part (steps 1–5) ─────

    /// <summary>Step 1: Reads the settings, checks them at start-up, and accepts lists written as one value.</summary>
    private static void AddValidatedOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IspCheckerOptions>().Bind(configuration).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<IspCheckerOptions>, IspCheckerOptionsValidator>());

        // .NET only fills list settings from numbered keys (TrustedProxies__0, __1, …). Also accept one plain value
        // (Tools__IspChecker__TrustedProxies=172.18.0.0/16, comma-separated for several), which is how these are
        // usually written in Docker and compose files.
        services.PostConfigure<IspCheckerOptions>(options =>
        {
            options.TrustedProxies = WithScalar(options.TrustedProxies, configuration[nameof(IspCheckerOptions.TrustedProxies)]);
            options.AllowedOrigins = WithScalar(options.AllowedOrigins, configuration[nameof(IspCheckerOptions.AllowedOrigins)]);
        });

        static string[] WithScalar(string[] values, string? scalar) =>
            string.IsNullOrWhiteSpace(scalar) ? values : [.. values, scalar];
    }

    /// <summary>Step 2: The file-based list of networks, the optional location list, and the checker that uses them.</summary>
    private static void AddLookup(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        // Relative file paths start from the app's folder.
        services.AddSingleton<IAsnDataSource>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<IspCheckerOptions>>().Value;
            return new IpToAsnFileDataSource(FullPath(sp, options.DataFilePath));
        });

        // An empty LocationDataFilePath turns location off.
        services.AddSingleton<ILocationDataSource>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<IspCheckerOptions>>().Value;
            return string.IsNullOrWhiteSpace(options.LocationDataFilePath)
                ? new NoLocationData()
                : new DbIpCityFileDataSource(FullPath(sp, options.LocationDataFilePath));
        });
        services.AddSingleton<IspChecker>();

        static string FullPath(IServiceProvider sp, string path) =>
            Path.GetFullPath(path, sp.GetRequiredService<IHostEnvironment>().ContentRootPath);
    }

    /// <summary>
    /// Step 3: The servers allowed to pass on the visitor's address in <c>X-Forwarded-For</c>. Only changed when
    /// <c>TrustedProxies</c> is set; otherwise .NET's default (only this computer) stays.
    /// </summary>
    private static void AddTrustedProxies(this IServiceCollection services)
    {
        services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<IspCheckerOptions>>((forwarded, isp) =>
        {
            if (!TrustedProxyList.TryParse(isp.Value.TrustedProxies, out var networks, out _) || networks.Count == 0)
            {
                return; // Invalid entries are reported by IspCheckerOptionsValidator at startup.
            }

            forwarded.KnownProxies.Clear();
            forwarded.KnownIPNetworks.Clear();
            foreach (var network in networks)
            {
                forwarded.KnownIPNetworks.Add(network);
            }

            // Walk back through every trusted server (e.g. Cloudflare → nginx → app) to the first untrusted address,
            // which is the visitor. Only trusted servers' entries are believed, so a visitor can't fake their address.
            forwarded.ForwardLimit = null;
        });
    }

    /// <summary>
    /// Step 4: Lets pages on the <c>AllowedOrigins</c> websites read the API's answers from the browser (CORS), for the
    /// API only. Read-only and anonymous: GET only, no cookies or logins. With no websites set, nothing is allowed.
    /// </summary>
    private static void AddCorsPolicy(this IServiceCollection services)
    {
        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<IOptions<IspCheckerOptions>>((cors, isp) =>
        {
            AllowedOriginList.TryParse(isp.Value.AllowedOrigins, out var origins, out _);
            cors.AddPolicy(IspCheckerEndpointRouteBuilderExtensions.CorsPolicy, policy =>
            {
                policy.WithMethods("GET").WithHeaders("Accept").SetPreflightMaxAge(TimeSpan.FromHours(1));

                if (origins.Contains("*"))
                {
                    policy.AllowAnyOrigin();
                }
                else if (origins.Count > 0)
                {
                    policy.WithOrigins([.. origins]);
                    if (origins.Any(o => o.Contains("://*.", StringComparison.Ordinal)))
                    {
                        policy.SetIsOriginAllowedToAllowWildcardSubdomains();
                    }
                }
            });
        });
    }

    /// <summary>
    /// Step 5: Limits typed-address lookups per visitor. The visitor's address is only kept in memory, to count their
    /// lookups in the current minute; it is never logged or stored.
    /// </summary>
    private static void AddRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = (rejected, _) => AnswerTooManyLookupsAsync(rejected);

            limiter.AddPolicy(IspCheckerEndpointRouteBuilderExtensions.LookupRateLimitPolicy, context =>
            {
                var options = context.RequestServices.GetRequiredService<IOptions<IspCheckerOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Max(1, options.LookupRequestsPerMinute),
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    });
            });
        });
    }

    /// <summary>
    /// Step 5: Answers a visitor who went over the limit, saying how many seconds to wait. Uses the same kind of error
    /// answer as the others, so calling pages can always read its "detail" message.
    /// </summary>
    private static async ValueTask AnswerTooManyLookupsAsync(OnRejectedContext rejected)
    {
        var response = rejected.HttpContext.Response;
        response.Headers.CacheControl = "no-store";

        var retryAfter = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
            ? (int)Math.Ceiling(wait.TotalSeconds)
            : 60;
        response.Headers.RetryAfter = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);

        await Results.Problem(
                $"Too many lookups. Try again in {retryAfter} seconds.",
                statusCode: StatusCodes.Status429TooManyRequests)
            .ExecuteAsync(rejected.HttpContext);
    }
}
