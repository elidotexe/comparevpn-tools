using System.Net;
using Moq;

namespace CompareVPN.Tools.IspChecker.Tests;

[TestFixture]
public class IspCheckerTests
{
    private static readonly DateTimeOffset UpdatedAt = new(2026, 11, 1, 6, 0, 0, TimeSpan.Zero);

    private static readonly AsnLookup Cloudflare = new(
        new AsnRecord(13335, "CLOUDFLARENET - Cloudflare, Inc.", "US"),
        IPAddress.Parse("1.1.1.0"), IPAddress.Parse("1.1.1.255"));

    private static readonly AsnLookup Comcast = new(
        new AsnRecord(7922, "COMCAST-7922 - Comcast Cable Communications, LLC", "US"),
        IPAddress.Parse("73.0.0.0"), IPAddress.Parse("73.255.255.255"));

    [TestCase("10.1.2.3")]
    [TestCase("172.20.0.1")]
    [TestCase("192.168.1.1")]
    [TestCase("127.0.0.1")]
    [TestCase("100.64.0.1")]
    [TestCase("169.254.10.10")]
    [TestCase("::1")]
    [TestCase("fe80::1")]
    [TestCase("fd12:3456::1")]
    public async Task CheckAsync_DoesNotQueryDataSource_ForNonPublicAddress(string ip)
    {
        var source = new Mock<IAsnDataSource>(MockBehavior.Strict);
        var checker = new IspChecker(source.Object);

        var result = await CheckAsync(checker, IPAddress.Parse(ip));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsPublic, Is.False);
            Assert.That(result.Network, Is.Null);
            Assert.That(result.DataUpdatedAt, Is.Null);
        });
    }

    [Test]
    public async Task CheckAsync_ReturnsDisplayReadyNetwork_ForPublicAddress()
    {
        var address = IPAddress.Parse("73.1.2.3");
        var source = new Mock<IAsnDataSource>();
        source.Setup(s => s.FindAsync(address, It.IsAny<CancellationToken>())).ReturnsAsync(AsnSearchResult.Found(Comcast, UpdatedAt));

        var result = await CheckAsync(new IspChecker(source.Object), address);

        Assert.That(result, Is.EqualTo(new IspCheckResult(
            "73.1.2.3",
            IpVersion: 4,
            IsPublic: true,
            new NetworkInfo(
                7922,
                "Comcast Xfinity",
                "COMCAST-7922 - Comcast Cable Communications, LLC",
                "US",
                new IpRange("73.0.0.0", "73.255.255.255"),
                IsHosting: false),
            UpdatedAt)));
    }

    [Test]
    public async Task CheckAsync_FlagsHostingNetworks()
    {
        var source = new Mock<IAsnDataSource>();
        source.Setup(s => s.FindAsync(It.IsAny<IPAddress>(), It.IsAny<CancellationToken>())).ReturnsAsync(AsnSearchResult.Found(Cloudflare, UpdatedAt));

        var result = await CheckAsync(new IspChecker(source.Object), IPAddress.Parse("1.1.1.1"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Network!.IsHosting, Is.True);
            Assert.That(result.Network.Name, Is.EqualTo("Cloudflare"));
        });
    }

    [TestCase(13335)]  // Cloudflare (WARP)
    [TestCase(209103)] // ProtonVPN
    [TestCase(16509)]  // AWS
    public void HostingNetworks_IncludesKnownVpnAndCloudNetworks(int asn)
    {
        Assert.That(HostingNetworks.Contains(asn), Is.True);
    }

    [TestCase(7922)]   // Comcast
    [TestCase(3320)]   // Deutsche Telekom
    public void HostingNetworks_ExcludesConsumerIsps(int asn)
    {
        Assert.That(HostingNetworks.Contains(asn), Is.False);
    }

    [Test]
    public async Task CheckAsync_ReturnsPublicWithoutNetwork_WhenNotInDataset()
    {
        var source = new Mock<IAsnDataSource>();
        source.Setup(s => s.FindAsync(It.IsAny<IPAddress>(), It.IsAny<CancellationToken>())).ReturnsAsync(AsnSearchResult.NotInDataset(UpdatedAt));

        var result = await CheckAsync(new IspChecker(source.Object), IPAddress.Parse("9.9.9.9"));

        Assert.Multiple(() =>
        {
            Assert.That(result.IsPublic, Is.True);
            Assert.That(result.Network, Is.Null);
        });
    }

    [Test]
    public async Task CheckAsync_ReportsDatasetDate_WhenAddressIsNotInDataset()
    {
        var source = new Mock<IAsnDataSource>();
        source.Setup(s => s.FindAsync(It.IsAny<IPAddress>(), It.IsAny<CancellationToken>())).ReturnsAsync(AsnSearchResult.NotInDataset(UpdatedAt));

        var result = await CheckAsync(new IspChecker(source.Object), IPAddress.Parse("1.0.1.1"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Network, Is.Null);
            Assert.That(result.DataUpdatedAt, Is.EqualTo(UpdatedAt));
        });
    }

    [Test]
    public async Task CheckAsync_NormalizesIpv4MappedIpv6()
    {
        var source = new Mock<IAsnDataSource>();
        source.Setup(s => s.FindAsync(IPAddress.Parse("1.1.1.1"), It.IsAny<CancellationToken>())).ReturnsAsync(AsnSearchResult.Found(Cloudflare, UpdatedAt));

        var result = await CheckAsync(new IspChecker(source.Object), IPAddress.Parse("::ffff:1.1.1.1"));

        Assert.Multiple(() =>
        {
            Assert.That(result.IpAddress, Is.EqualTo("1.1.1.1"));
            Assert.That(result.IpVersion, Is.EqualTo(4));
            Assert.That(result.Network!.Asn, Is.EqualTo(13335));
        });
    }

    [Test]
    public async Task CheckAsync_ReportsIpVersion6()
    {
        var source = new Mock<IAsnDataSource>();
        source.Setup(s => s.FindAsync(It.IsAny<IPAddress>(), It.IsAny<CancellationToken>())).ReturnsAsync(AsnSearchResult.NotInDataset(UpdatedAt));
        var result = await CheckAsync(new IspChecker(source.Object), IPAddress.Parse("2001:4860:4860::8888"));

        Assert.That(result.IpVersion, Is.EqualTo(6));
    }

    [Test]
    public async Task CheckAsync_ReportsDataNotAvailable_WhenNoDatasetIsLoaded()
    {
        var source = new Mock<IAsnDataSource>();
        source.Setup(s => s.FindAsync(It.IsAny<IPAddress>(), It.IsAny<CancellationToken>())).ReturnsAsync(AsnSearchResult.DataNotAvailable());

        var answer = await new IspChecker(source.Object).CheckAsync(IPAddress.Parse("73.1.2.3"));

        Assert.Multiple(() =>
        {
            Assert.That(answer.Outcome, Is.EqualTo(IspCheckOutcome.DataNotAvailable));
            Assert.That(answer.Result, Is.Null);
        });
    }

    [Test]
    public async Task CheckAsync_AnswersPrivateAddresses_EvenWhenNoDatasetIsLoaded()
    {
        var source = new Mock<IAsnDataSource>();
        source.Setup(s => s.FindAsync(It.IsAny<IPAddress>(), It.IsAny<CancellationToken>())).ReturnsAsync(AsnSearchResult.DataNotAvailable());

        var answer = await new IspChecker(source.Object).CheckAsync(IPAddress.Parse("192.168.1.1"));

        Assert.That(answer.Outcome, Is.EqualTo(IspCheckOutcome.Checked));
    }

    [Test]
    public async Task CheckAsync_AddsLocationRoundedToTwoDecimals_ForPublicAddress()
    {
        var address = IPAddress.Parse("73.1.2.3");
        var locations = new Mock<ILocationDataSource>();
        locations.Setup(l => l.FindAsync(address, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IpLocation("Plant City", "Florida", "US", 28.0186, -82.1129));

        var result = await CheckAsync(new IspChecker(NetworkSource(Comcast).Object, locations.Object), address);

        Assert.That(result.Location, Is.EqualTo(new IpLocation("Plant City", "Florida", "US", 28.02, -82.11)));
    }

    [Test]
    public async Task CheckAsync_AddsLocation_EvenWhenTheNetworkIsUnknown()
    {
        var networks = new Mock<IAsnDataSource>();
        networks.Setup(s => s.FindAsync(It.IsAny<IPAddress>(), It.IsAny<CancellationToken>())).ReturnsAsync(AsnSearchResult.NotInDataset(UpdatedAt));
        var locations = new Mock<ILocationDataSource>();
        locations.Setup(l => l.FindAsync(It.IsAny<IPAddress>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IpLocation(null, null, "GB", null, null));

        var result = await CheckAsync(new IspChecker(networks.Object, locations.Object), IPAddress.Parse("81.2.69.160"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Network, Is.Null);
            Assert.That(result.Location, Is.EqualTo(new IpLocation(null, null, "GB", null, null)));
        });
    }

    [Test]
    public async Task CheckAsync_LooksUpLocationForTheTidiedAddress()
    {
        var locations = new Mock<ILocationDataSource>();
        locations.Setup(l => l.FindAsync(IPAddress.Parse("1.1.1.1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IpLocation("Sydney", "New South Wales", "AU", -33.8688, 151.209));

        var result = await CheckAsync(new IspChecker(NetworkSource(Cloudflare).Object, locations.Object), IPAddress.Parse("::ffff:1.1.1.1"));

        Assert.That(result.Location?.City, Is.EqualTo("Sydney"));
    }

    [Test]
    public async Task CheckAsync_HasNoLocation_WhenTheLocationListDoesNotKnowTheAddress()
    {
        var locations = new Mock<ILocationDataSource>();
        locations.Setup(l => l.FindAsync(It.IsAny<IPAddress>(), It.IsAny<CancellationToken>())).ReturnsAsync((IpLocation?)null);

        var result = await CheckAsync(new IspChecker(NetworkSource(Comcast).Object, locations.Object), IPAddress.Parse("73.1.2.3"));

        Assert.That(result.Location, Is.Null);
    }

    [Test]
    public async Task CheckAsync_HasNoLocation_WithoutALocationSource()
    {
        var result = await CheckAsync(new IspChecker(NetworkSource(Comcast).Object), IPAddress.Parse("73.1.2.3"));

        Assert.That(result.Location, Is.Null);
    }

    [Test]
    public async Task CheckAsync_DoesNotLookUpLocation_ForPrivateAddress()
    {
        var locations = new Mock<ILocationDataSource>(MockBehavior.Strict);

        var result = await CheckAsync(new IspChecker(new Mock<IAsnDataSource>(MockBehavior.Strict).Object, locations.Object), IPAddress.Parse("192.168.1.1"));

        Assert.That(result.Location, Is.Null);
    }

    [Test]
    public async Task CheckAsync_ReportsDataNotAvailable_EvenWhenALocationIsKnown()
    {
        var networks = new Mock<IAsnDataSource>();
        networks.Setup(s => s.FindAsync(It.IsAny<IPAddress>(), It.IsAny<CancellationToken>())).ReturnsAsync(AsnSearchResult.DataNotAvailable());
        var locations = new Mock<ILocationDataSource>();
        locations.Setup(l => l.FindAsync(It.IsAny<IPAddress>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IpLocation("Plant City", "Florida", "US", 28.0186, -82.1129));

        var answer = await new IspChecker(networks.Object, locations.Object).CheckAsync(IPAddress.Parse("73.1.2.3"));

        Assert.That(answer.Outcome, Is.EqualTo(IspCheckOutcome.DataNotAvailable));
    }

    private static Mock<IAsnDataSource> NetworkSource(AsnLookup match)
    {
        var source = new Mock<IAsnDataSource>();
        source.Setup(s => s.FindAsync(It.IsAny<IPAddress>(), It.IsAny<CancellationToken>())).ReturnsAsync(AsnSearchResult.Found(match, UpdatedAt));
        return source;
    }

    /// <summary>Checks an address and returns the result, after confirming the checker could answer.</summary>
    private static async Task<IspCheckResult> CheckAsync(IspChecker checker, IPAddress address)
    {
        var answer = await checker.CheckAsync(address);
        Assert.That(answer.Outcome, Is.EqualTo(IspCheckOutcome.Checked));
        return answer.Result!;
    }
}
