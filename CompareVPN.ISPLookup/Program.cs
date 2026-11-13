using CompareVPN.Tools.IspChecker.AspNetCore;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Also configures trusted proxies (TrustedProxies), CORS (AllowedOrigins), dataset reloading and health checks.
builder.Services.AddIspChecker(builder.Configuration.GetSection("Tools:IspChecker"));

// Read the visitor IP from X-Forwarded-For, but only when it comes from a trusted proxy:
// loopback by default, or the networks listed in Tools:IspChecker:TrustedProxies.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseCors();          // before the rate limiter, so 429 responses carry CORS headers too
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapIspChecker("/api/tools/isp");
app.MapHealthChecks("/healthz");

app.Run();
