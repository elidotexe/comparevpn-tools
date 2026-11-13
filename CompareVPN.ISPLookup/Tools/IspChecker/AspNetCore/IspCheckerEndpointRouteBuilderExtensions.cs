using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CompareVPN.Tools.IspChecker.AspNetCore;

/// <summary>
/// This class sets up the ISP checker's web addresses: the front desk where browsers ask their questions.
///
/// In plain terms, here is what happens:
///   1. "Who is my provider?": a visitor asks about their own connection, and we look up the address the request
///      came from.
///   2. "Who owns this address?": a visitor types any address. We check that it really is an address before looking
///      it up. Each visitor may only do this a limited number of times per minute.
///   Both answers tell browsers and caches not to keep a copy, and addresses are never written to the logs. If the list
///   of networks hasn't been downloaded yet, both answer "not available yet", so the visitor can try again later.
/// </summary>
public static class IspCheckerEndpointRouteBuilderExtensions
{
    /// <summary>Rate-limit policy for <c>GET {pattern}/{ip}</c>. Requires <c>app.UseRateLimiter()</c>.</summary>
    public const string LookupRateLimitPolicy = "IspChecker.Lookup";

    /// <summary>CORS policy for both web addresses. Requires <c>app.UseCors()</c> before <c>app.UseRateLimiter()</c>.</summary>
    public const string CorsPolicy = "IspChecker.Cors";

    /// <summary>
    /// Sets up <c>GET {pattern}</c> (step 1, the visitor's own provider) and <c>GET {pattern}/{ip}</c> (step 2, any
    /// address, rate limited).
    /// </summary>
    /// <remarks>
    /// Step 1 uses the address the request came from. Behind a reverse proxy (Cloudflare, nginx, …) call
    /// <c>app.UseForwardedHeaders()</c> first, otherwise every visitor is reported as the proxy's network.
    /// </remarks>
    public static RouteGroupBuilder MapIspChecker(this IEndpointRouteBuilder endpoints, string pattern = "/api/tools/isp")
    {
        var group = endpoints.MapGroup(pattern).RequireCors(CorsPolicy);

        group.MapGet("/", CheckVisitorAsync)
            .WithName("IspCheckerSelf")
            .Produces<IspCheckResult>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/{ip}", CheckTypedAddressAsync)
            .WithName("IspCheckerLookup")
            .RequireRateLimiting(LookupRateLimitPolicy)
            .Produces<IspCheckResult>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return group;
    }

    /// <summary>Step 1: Looks up the provider of the address the request came from.</summary>
    private static async Task<IResult> CheckVisitorAsync(HttpContext context, IspChecker checker, CancellationToken cancellationToken)
    {
        // Step 1a: Ask browsers and caches not to keep a copy; the answer is personal.
        DoNotStore(context);

        // Step 1b: Without the visitor's address there is nothing to look up.
        if (context.Connection.RemoteIpAddress is not { } address)
        {
            return Results.Problem("Could not determine your IP address.", statusCode: StatusCodes.Status400BadRequest);
        }

        // Step 1c: Look it up and answer.
        return ToResponse(await checker.CheckAsync(address, cancellationToken));
    }

    /// <summary>Step 2: Looks up the provider of an address the visitor typed.</summary>
    private static async Task<IResult> CheckTypedAddressAsync(
        string ip, HttpContext context, IspChecker checker, CancellationToken cancellationToken)
    {
        // Step 2a: Ask browsers and caches not to keep a copy.
        DoNotStore(context);

        // Step 2b: Only plain IPv4 and IPv6 addresses are accepted.
        if (!IpAddressInput.TryParse(ip, out var address))
        {
            return Results.Problem(
                "Enter a valid IPv4 (e.g. 8.8.8.8) or IPv6 (e.g. 2001:4860::8888) address.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Step 2c: Look it up and answer.
        return ToResponse(await checker.CheckAsync(address, cancellationToken));
    }

    // ───── Helpers for both lookups (steps 1–2) ─────

    private static void DoNotStore(HttpContext context) => context.Response.Headers.CacheControl = "no-store";

    /// <summary>Turns the checker's answer into the response: the result, or "try again later" while no data is loaded.</summary>
    private static IResult ToResponse(IspCheckAnswer answer) =>
        answer switch
        {
            { Outcome: IspCheckOutcome.Checked, Result: { } result } => Results.Ok(result),
            { Outcome: IspCheckOutcome.DataNotAvailable } =>
                Results.Problem("ISP data is not available yet.", statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => throw new InvalidOperationException($"Unexpected ISP check outcome: {answer.Outcome}"),
        };
}
