// The ISP lookup website: the API that tells visitors which internet provider is behind an address, plus a small
// demo page.
//
// In plain terms, here is what happens when it starts:
//   1. It sets up the ISP checker from the Tools:IspChecker settings: the list of networks and its daily reload,
//      trusted proxies, which websites may call the API, the lookup limit and the health check.
//   2. It reads the visitor's real address from X-Forwarded-For, but only when a trusted proxy sent it.
//   3. It puts the request steps in order, then opens the API at /api/tools/isp and the health check at /healthz.

using CompareVPN.Tools.IspChecker.AspNetCore;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Step 1: The ISP checker and everything it needs.
builder.Services.AddIspChecker(builder.Configuration.GetSection("Tools:IspChecker"));

// Step 2: Which proxies count as trusted is set by AddIspChecker (only this computer, unless TrustedProxies says otherwise).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();

// Step 3: Request steps, in the order each request passes through them.
app.UseForwardedHeaders();
app.UseCors(); // Before the rate limiter, so "too many lookups" answers can still be read by the calling website.
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapIspChecker("/api/tools/isp");
app.MapHealthChecks("/healthz");

app.Run();
