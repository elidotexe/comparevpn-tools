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

/// <summary>Registers the ISP checker services.</summary>
public static class IspCheckerServiceCollectionExtensions
{
    /// <summary>
    /// Adds the ISP checker with options bound from <paramref name="configuration"/> (the <c>Tools:IspChecker</c> section),
    /// plus the trusted proxies, CORS policy, rate limiting, dataset reloading and health check it needs.
    /// </summary>
    public static IServiceCollection AddIspChecker(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions(configuration);
        services.AddLookup();
        services.AddTrustedProxies();
        services.AddCorsPolicy();
        services.AddRateLimiting();

        services.AddHostedService<IspCheckerDatasetService>();
        services.AddHealthChecks().AddCheck<IspCheckerHealthCheck>("isp-dataset");

        return services;
    }

    private static void AddValidatedOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IspCheckerOptions>().Bind(configuration).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<IspCheckerOptions>, IspCheckerOptionsValidator>());

        // The binder only fills arrays from indexed keys (TrustedProxies__0). Also accept a plain value
        // (Tools__IspChecker__TrustedProxies=172.18.0.0/16, comma-separated for several), which is how
        // these are usually written in Docker / compose environments.
        services.PostConfigure<IspCheckerOptions>(options =>
        {
            options.TrustedProxies = WithScalar(options.TrustedProxies, configuration[nameof(IspCheckerOptions.TrustedProxies)]);
            options.AllowedOrigins = WithScalar(options.AllowedOrigins, configuration[nameof(IspCheckerOptions.AllowedOrigins)]);
        });

        static string[] WithScalar(string[] values, string? scalar) =>
            string.IsNullOrWhiteSpace(scalar) ? values : [.. values, scalar];
    }

    private static void AddLookup(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IAsnDataSource>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<IspCheckerOptions>>().Value;
            var contentRoot = sp.GetRequiredService<IHostEnvironment>().ContentRootPath;
            return new IpToAsnFileDataSource(Path.GetFullPath(options.DataFilePath, contentRoot));
        });
        services.AddSingleton<IIspChecker, Tools.IspChecker.IspChecker>();
    }

    /// <summary>
    /// Trusted reverse proxies for X-Forwarded-For. Only applied when configured; otherwise the
    /// ASP.NET Core default (loopback only) stays. The app still has to call <c>UseForwardedHeaders()</c>.
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

            // Walk back through every trusted hop (e.g. Cloudflare → nginx → app) to the first untrusted
            // address, which is the visitor. Only trusted proxies can add entries, so this is not spoofable.
            forwarded.ForwardLimit = null;
        });
    }

    /// <summary>
    /// CORS for the API endpoints only, so browser JavaScript on the configured sites can read responses.
    /// Read-only and anonymous: GET only, no cookies or credentials. With no origins configured the
    /// policy allows nothing and responses stay same-origin.
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
    /// Limits the "look up any IP" endpoint per client. The client address is only held in memory
    /// as a partition key for the current window; it is never logged or persisted.
    /// </summary>
    private static void AddRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Same problem+json shape as the other errors, so clients can always read { detail }.
            limiter.OnRejected = async (rejected, cancellationToken) =>
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
            };

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
}
