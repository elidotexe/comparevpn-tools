using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CompareVPN.Tools.IspChecker.Tests;

/// <summary>
/// Calls the running app the way a browser would, to pin down what the public API answers:
/// status codes, the response body, the no-store header, rate limiting, CORS and forwarded addresses.
/// </summary>
[TestFixture]
public class IspCheckerApiTests
{
    private const string Dataset =
        "73.0.0.0\t73.255.255.255\t7922\tUS\tCOMCAST-7922 - Comcast Cable Communications, LLC\n" +
        "1.1.1.0\t1.1.1.255\t13335\tUS\tCLOUDFLARENET - Cloudflare, Inc.\n";

    // Test-only header that sets the address the request appears to come from (the test server has none).
    private const string ClientIpHeader = "X-Test-Client-Ip";

    private string _directory = null!;
    private string _datasetPath = null!;
    private string _locationPath = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("ispchecker-api-tests").FullName;
        _datasetPath = Path.Combine(_directory, "ip2asn-combined.tsv");
        File.WriteAllText(_datasetPath, Dataset);

        // Not written unless a test needs it, so the tests never pick up a real location file on this computer.
        _locationPath = Path.Combine(_directory, "dbip-city-lite.mmdb");
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [Test]
    public async Task Lookup_ReturnsNetwork_ForPublicAddressInDataset()
    {
        using var app = StartApp();

        var response = await app.CreateClient().GetAsync("/api/tools/isp/73.1.2.3");
        var body = await ReadJson(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(body.GetProperty("ipAddress").GetString(), Is.EqualTo("73.1.2.3"));
            Assert.That(body.GetProperty("ipVersion").GetInt32(), Is.EqualTo(4));
            Assert.That(body.GetProperty("isPublic").GetBoolean(), Is.True);
            var network = body.GetProperty("network");
            Assert.That(network.GetProperty("asn").GetInt32(), Is.EqualTo(7922));
            Assert.That(network.GetProperty("name").GetString(), Is.EqualTo("Comcast Xfinity"));
            Assert.That(network.GetProperty("organization").GetString(), Is.EqualTo("COMCAST-7922 - Comcast Cable Communications, LLC"));
            Assert.That(network.GetProperty("countryCode").GetString(), Is.EqualTo("US"));
            Assert.That(network.GetProperty("range").GetProperty("start").GetString(), Is.EqualTo("73.0.0.0"));
            Assert.That(network.GetProperty("range").GetProperty("end").GetString(), Is.EqualTo("73.255.255.255"));
            Assert.That(network.GetProperty("isHosting").GetBoolean(), Is.False);
            Assert.That(body.GetProperty("dataUpdatedAt").ValueKind, Is.EqualTo(JsonValueKind.String));
        });
    }

    [Test]
    public async Task Lookup_IncludesLocation_WhenTheLocationFileIsThere()
    {
        TestLocationFile.Write(_locationPath, ("73.1.0.0/16", TestLocationFile.Place("Plant City", "Florida", "US", 28.0186, -82.1129)));
        using var app = StartApp();

        var body = await ReadJsonOnceLocationIsLoaded(app, "/api/tools/isp/73.1.2.3");

        var location = body.GetProperty("location");
        Assert.Multiple(() =>
        {
            Assert.That(location.GetProperty("city").GetString(), Is.EqualTo("Plant City"));
            Assert.That(location.GetProperty("region").GetString(), Is.EqualTo("Florida"));
            Assert.That(location.GetProperty("countryCode").GetString(), Is.EqualTo("US"));
            Assert.That(location.GetProperty("latitude").GetDouble(), Is.EqualTo(28.02));
            Assert.That(location.GetProperty("longitude").GetDouble(), Is.EqualTo(-82.11));
            Assert.That(body.GetProperty("network").GetProperty("asn").GetInt32(), Is.EqualTo(7922));
        });
    }

    [Test]
    public async Task Lookup_AnswersWithNullLocation_WhenTheLocationFileIsMissing()
    {
        using var app = StartApp();

        var response = await app.CreateClient().GetAsync("/api/tools/isp/73.1.2.3");
        var body = await ReadJson(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body.GetProperty("location").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(body.GetProperty("network").GetProperty("asn").GetInt32(), Is.EqualTo(7922));
        });
    }

    [Test]
    public async Task Lookup_AnswersWithNullLocation_WhenLocationIsTurnedOff()
    {
        TestLocationFile.Write(_locationPath, ("73.1.0.0/16", TestLocationFile.Place("Plant City", "Florida", "US", 28.0186, -82.1129)));
        using var app = StartApp(("LocationDataFilePath", ""));

        var body = await ReadJson(await app.CreateClient().GetAsync("/api/tools/isp/73.1.2.3"));

        Assert.That(body.GetProperty("location").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public async Task Health_StaysHealthy_WithoutTheLocationFile()
    {
        using var app = StartApp();
        var client = app.CreateClient();
        await client.GetAsync("/api/tools/isp/73.1.2.3"); // Loads the list of networks.

        var response = await client.GetAsync("/healthz");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Lookup_ReturnsPublicWithoutNetwork_ForAddressNotInDataset()
    {
        using var app = StartApp();

        var body = await ReadJson(await app.CreateClient().GetAsync("/api/tools/isp/9.9.9.9"));

        Assert.Multiple(() =>
        {
            Assert.That(body.GetProperty("isPublic").GetBoolean(), Is.True);
            Assert.That(body.GetProperty("network").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(body.GetProperty("dataUpdatedAt").ValueKind, Is.EqualTo(JsonValueKind.String));
        });
    }

    [Test]
    public async Task Lookup_ReturnsNotPublic_ForPrivateAddress()
    {
        using var app = StartApp();

        var response = await app.CreateClient().GetAsync("/api/tools/isp/192.168.1.1");
        var body = await ReadJson(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body.GetProperty("isPublic").GetBoolean(), Is.False);
            Assert.That(body.GetProperty("network").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(body.GetProperty("dataUpdatedAt").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
    }

    [TestCase("not-an-ip")]
    [TestCase("8.8")]
    public async Task Lookup_Returns400_ForInvalidAddress(string ip)
    {
        using var app = StartApp();

        var response = await app.CreateClient().GetAsync($"/api/tools/isp/{ip}");
        var body = await ReadJson(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(body.GetProperty("detail").GetString(), Does.StartWith("Enter a valid IPv4"));
        });
    }

    [Test]
    public async Task Lookup_Returns503_WhenDatasetIsMissing()
    {
        File.Delete(_datasetPath);
        using var app = StartApp();

        var response = await app.CreateClient().GetAsync("/api/tools/isp/73.1.2.3");
        var body = await ReadJson(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(body.GetProperty("detail").GetString(), Is.EqualTo("ISP data is not available yet."));
        });
    }

    [Test]
    public async Task Lookup_AnswersOnceDatasetAppears_AfterStartingWithout()
    {
        File.Delete(_datasetPath);
        using var app = StartApp();
        var client = app.CreateClient();
        Assert.That((await client.GetAsync("/api/tools/isp/73.1.2.3")).StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));

        await File.WriteAllTextAsync(_datasetPath, Dataset);

        Assert.That((await client.GetAsync("/api/tools/isp/73.1.2.3")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Visitor_ReturnsCallersNetwork()
    {
        using var app = StartApp();

        var body = await ReadJson(await Send(app, "/api/tools/isp", clientIp: "73.1.2.3"));

        Assert.That(body.GetProperty("network").GetProperty("asn").GetInt32(), Is.EqualTo(7922));
    }

    [Test]
    public async Task Visitor_Returns400_WhenCallersAddressIsUnknown()
    {
        using var app = StartApp();

        var response = await app.CreateClient().GetAsync("/api/tools/isp");
        var body = await ReadJson(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(body.GetProperty("detail").GetString(), Is.EqualTo("Could not determine your IP address."));
        });
    }

    [Test]
    public async Task Visitor_UsesForwardedAddress_FromTrustedProxy()
    {
        using var app = StartApp();

        var body = await ReadJson(await Send(app, "/api/tools/isp", clientIp: "127.0.0.1", forwardedFor: "73.1.2.3"));

        Assert.That(body.GetProperty("ipAddress").GetString(), Is.EqualTo("73.1.2.3"));
    }

    [Test]
    public async Task Visitor_IgnoresForwardedAddress_FromUntrustedCaller()
    {
        using var app = StartApp();

        var body = await ReadJson(await Send(app, "/api/tools/isp", clientIp: "1.1.1.1", forwardedFor: "73.1.2.3"));

        Assert.That(body.GetProperty("ipAddress").GetString(), Is.EqualTo("1.1.1.1"));
    }

    [Test]
    public async Task Lookup_Returns429WithRetryAfter_WhenOverTheLimit()
    {
        using var app = StartApp(("LookupRequestsPerMinute", "2"));

        await Send(app, "/api/tools/isp/73.1.2.3", clientIp: "1.1.1.1");
        await Send(app, "/api/tools/isp/73.1.2.3", clientIp: "1.1.1.1");
        var response = await Send(app, "/api/tools/isp/73.1.2.3", clientIp: "1.1.1.1");
        var body = await ReadJson(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(response.Headers.RetryAfter, Is.Not.Null);
            Assert.That(body.GetProperty("detail").GetString(), Does.StartWith("Too many lookups."));
        });
    }

    [Test]
    public async Task Lookup_LimitsEachCallerSeparately()
    {
        using var app = StartApp(("LookupRequestsPerMinute", "1"));

        await Send(app, "/api/tools/isp/73.1.2.3", clientIp: "1.1.1.1");
        var otherCaller = await Send(app, "/api/tools/isp/73.1.2.3", clientIp: "1.1.1.2");

        Assert.That(otherCaller.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Visitor_IsNotRateLimited()
    {
        using var app = StartApp(("LookupRequestsPerMinute", "1"));

        await Send(app, "/api/tools/isp", clientIp: "73.1.2.3");
        var second = await Send(app, "/api/tools/isp", clientIp: "73.1.2.3");

        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Cors_AllowsConfiguredOrigin_AlsoOnRateLimitedResponses()
    {
        using var app = StartApp(("AllowedOrigins", "https://comparevpn.co"), ("LookupRequestsPerMinute", "1"));

        var allowed = await Send(app, "/api/tools/isp/73.1.2.3", clientIp: "1.1.1.1", origin: "https://comparevpn.co");
        var limited = await Send(app, "/api/tools/isp/73.1.2.3", clientIp: "1.1.1.1", origin: "https://comparevpn.co");

        Assert.Multiple(() =>
        {
            Assert.That(allowed.Headers.GetValues("Access-Control-Allow-Origin"), Is.EqualTo(new[] { "https://comparevpn.co" }));
            Assert.That(limited.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(limited.Headers.GetValues("Access-Control-Allow-Origin"), Is.EqualTo(new[] { "https://comparevpn.co" }));
        });
    }

    [Test]
    public async Task Cors_SendsNoAllowHeader_ForOtherOrigins()
    {
        using var app = StartApp(("AllowedOrigins", "https://comparevpn.co"));

        var response = await Send(app, "/api/tools/isp/73.1.2.3", origin: "https://elsewhere.example");

        Assert.That(response.Headers.Contains("Access-Control-Allow-Origin"), Is.False);
    }

    [Test]
    public async Task Health_IsUnhealthy_WhenDatasetIsMissing()
    {
        File.Delete(_datasetPath);
        using var app = StartApp();

        var response = await app.CreateClient().GetAsync("/healthz");

        Assert.Multiple(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("Unhealthy"));
        });
    }

    [Test]
    public async Task Health_IsHealthy_OnceDatasetIsLoaded()
    {
        using var app = StartApp();
        var client = app.CreateClient();

        // A lookup waits for the first load, so the health check sees the loaded data afterwards.
        await client.GetAsync("/api/tools/isp/73.1.2.3");
        var response = await client.GetAsync("/healthz");

        Assert.Multiple(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("Healthy"));
        });
    }

    [Test]
    public async Task Settings_AcceptTrustedProxiesAsOneCommaSeparatedValue()
    {
        using var app = StartApp(("TrustedProxies", "10.0.0.0/8, 192.168.0.0/16"));

        var body = await ReadJson(await Send(app, "/api/tools/isp", clientIp: "192.168.5.5", forwardedFor: "73.1.2.3"));

        Assert.That(body.GetProperty("ipAddress").GetString(), Is.EqualTo("73.1.2.3"));
    }

    [Test]
    public async Task Settings_TrustedProxiesReplaceTheLoopbackDefault()
    {
        using var app = StartApp(("TrustedProxies", "10.0.0.0/8"));

        var body = await ReadJson(await Send(app, "/api/tools/isp", clientIp: "127.0.0.1", forwardedFor: "73.1.2.3"));

        Assert.That(body.GetProperty("ipAddress").GetString(), Is.EqualTo("127.0.0.1"));
    }

    [Test]
    public async Task Settings_WalkBackThroughSeveralTrustedProxies()
    {
        using var app = StartApp(("TrustedProxies", "10.0.0.0/8"));

        var body = await ReadJson(await Send(app, "/api/tools/isp", clientIp: "10.0.0.1", forwardedFor: "73.1.2.3, 10.0.0.2"));

        Assert.That(body.GetProperty("ipAddress").GetString(), Is.EqualTo("73.1.2.3"));
    }

    [Test]
    public async Task Cors_AllowsSubdomains_OfAWildcardOrigin()
    {
        using var app = StartApp(("AllowedOrigins", "https://*.comparevpn.co"));

        var subdomain = await Send(app, "/api/tools/isp/73.1.2.3", origin: "https://www.comparevpn.co");
        var other = await Send(app, "/api/tools/isp/73.1.2.3", origin: "https://comparevpn.example");

        Assert.Multiple(() =>
        {
            Assert.That(subdomain.Headers.GetValues("Access-Control-Allow-Origin"), Is.EqualTo(new[] { "https://www.comparevpn.co" }));
            Assert.That(other.Headers.Contains("Access-Control-Allow-Origin"), Is.False);
        });
    }

    [Test]
    public async Task Cors_AllowsAnySite_WithStar()
    {
        using var app = StartApp(("AllowedOrigins", "*"));

        var response = await Send(app, "/api/tools/isp/73.1.2.3", origin: "https://anything.example");

        Assert.That(response.Headers.GetValues("Access-Control-Allow-Origin"), Is.EqualTo(new[] { "*" }));
    }

    [Test]
    public async Task Cors_IsOff_WhenNoOriginsAreConfigured()
    {
        using var app = StartApp();

        var response = await Send(app, "/api/tools/isp/73.1.2.3", origin: "https://comparevpn.co");

        Assert.That(response.Headers.Contains("Access-Control-Allow-Origin"), Is.False);
    }

    [Test]
    public async Task Cors_DoesNotApplyToTheHealthCheck()
    {
        using var app = StartApp(("AllowedOrigins", "*"));

        var response = await Send(app, "/healthz", origin: "https://anything.example");

        Assert.That(response.Headers.Contains("Access-Control-Allow-Origin"), Is.False);
    }

    [Test]
    public void Startup_FailsWithTheBadEntry_ForAnInvalidSetting()
    {
        using var app = StartApp(("TrustedProxies", "nginx"));

        var error = Assert.Catch(() => app.CreateClient());

        Assert.That(error!.ToString(), Does.Contain("'nginx'"));
    }

    // ───── Helpers ─────

    private WebApplicationFactory<Program> StartApp(params (string Name, string Value)[] settings)
    {
        var values = new Dictionary<string, string?>
        {
            ["Tools:IspChecker:DataFilePath"] = _datasetPath,
            ["Tools:IspChecker:LocationDataFilePath"] = _locationPath,
            ["Tools:IspChecker:ReloadCheckInterval"] = "00:00:00",
        };
        foreach (var (name, value) in settings)
        {
            values[$"Tools:IspChecker:{name}"] = value;
        }

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(values));
            builder.ConfigureServices(services => services.AddTransient<IStartupFilter, ClientIpFromHeader>());
        });
    }

    private static Task<HttpResponseMessage> Send(
        WebApplicationFactory<Program> app, string path, string? clientIp = null, string? forwardedFor = null, string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (clientIp is not null) request.Headers.Add(ClientIpHeader, clientIp);
        if (forwardedFor is not null) request.Headers.Add("X-Forwarded-For", forwardedFor);
        if (origin is not null) request.Headers.Add("Origin", origin);
        return app.CreateClient().SendAsync(request);
    }

    /// <summary>
    /// The location file is opened in the background after start-up, so ask until the answer has a location (or give
    /// up after a few seconds and return the last answer).
    /// </summary>
    private static async Task<JsonElement> ReadJsonOnceLocationIsLoaded(WebApplicationFactory<Program> app, string path)
    {
        var client = app.CreateClient();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            var body = await ReadJson(await client.GetAsync(path));
            if (body.GetProperty("location").ValueKind != JsonValueKind.Null || DateTime.UtcNow > deadline)
            {
                return body;
            }

            await Task.Delay(50);
        }
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    /// <summary>Runs before the app's own steps and sets the caller's address from <see cref="ClientIpHeader"/>.</summary>
    private sealed class ClientIpFromHeader : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((HttpContext context, RequestDelegate nextStep) =>
            {
                if (IPAddress.TryParse(context.Request.Headers[ClientIpHeader], out var address))
                {
                    context.Connection.RemoteIpAddress = address;
                }

                return nextStep(context);
            });
            next(app);
        };
    }
}
