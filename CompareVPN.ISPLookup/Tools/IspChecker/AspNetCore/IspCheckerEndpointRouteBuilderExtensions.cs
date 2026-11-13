using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CompareVPN.Tools.IspChecker.AspNetCore;

/// <summary>Maps the ISP checker HTTP endpoints.</summary>
public static class IspCheckerEndpointRouteBuilderExtensions
{
    /// <summary>Rate-limit policy applied to <c>GET {pattern}/{ip}</c>. Requires <c>app.UseRateLimiter()</c>.</summary>
    public const string LookupRateLimitPolicy = "IspChecker.Lookup";

    /// <summary>CORS policy applied to both endpoints. Requires <c>app.UseCors()</c> before <c>app.UseRateLimiter()</c>.</summary>
    public const string CorsPolicy = "IspChecker.Cors";

    /// <summary>
    /// Maps <c>GET {pattern}</c> (the calling visitor's ISP) and <c>GET {pattern}/{ip}</c> (any IP, rate limited).
    /// </summary>
    /// <remarks>
    /// The visitor endpoint reads <see cref="ConnectionInfo.RemoteIpAddress"/>. Behind a reverse proxy
    /// (Cloudflare, Azure Front Door, nginx) call <c>app.UseForwardedHeaders()</c> first,
    /// otherwise every visitor is reported as the proxy's network.
    /// Responses are marked <c>no-store</c> and addresses are never logged.
    /// </remarks>
    public static RouteGroupBuilder MapIspChecker(this IEndpointRouteBuilder endpoints, string pattern = "/api/tools/isp")
    {
        var group = endpoints.MapGroup(pattern).RequireCors(CorsPolicy);

        group.MapGet("/", (HttpContext context, IIspChecker checker, CancellationToken cancellationToken) =>
                CheckAsync(context, checker, context.Connection.RemoteIpAddress, "Could not determine your IP address.", cancellationToken))
            .WithName("IspCheckerSelf")
            .Produces<IspCheckResult>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/{ip}", (string ip, HttpContext context, IIspChecker checker, CancellationToken cancellationToken) =>
                CheckAsync(context, checker, IpAddressInput.TryParse(ip, out var address) ? address : null,
                    "Enter a valid IPv4 (e.g. 8.8.8.8) or IPv6 (e.g. 2001:4860::8888) address.", cancellationToken))
            .WithName("IspCheckerLookup")
            .RequireRateLimiting(LookupRateLimitPolicy)
            .Produces<IspCheckResult>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return group;
    }

    private static async Task<IResult> CheckAsync(
        HttpContext context, IIspChecker checker, IPAddress? address, string invalidMessage, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";

        if (address is null)
        {
            return Results.Problem(invalidMessage, statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            return Results.Ok(await checker.CheckAsync(address, cancellationToken));
        }
        catch (FileNotFoundException)
        {
            return Results.Problem("ISP data is not available yet.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
