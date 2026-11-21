using System.IO.Compression;
using System.Net;
using CompareVPN.Data.Networks;

namespace CompareVPN.Tools.IspChecker.Tests;

[TestFixture]
public class IpToAsnFileDataSourceTests
{
    // Deliberately unsorted, with an unrouted (ASN 0) row and a malformed row.
    private const string Dataset =
        "8.8.8.0\t8.8.8.255\t15169\tUS\tGOOGLE\n" +
        "1.0.0.0\t1.0.0.255\t13335\tUS\tCLOUDFLARENET\n" +
        "1.0.1.0\t1.0.3.255\t0\tNone\tNot routed\n" +
        "this is not a valid line\n" +
        "2001:4860::\t2001:4860:ffff:ffff:ffff:ffff:ffff:ffff\t15169\tUS\tGOOGLE\n";

    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("ispchecker-tests").FullName;
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [TestCase("1.0.0.1", 13335, "CLOUDFLARENET")]
    [TestCase("1.0.0.255", 13335, "CLOUDFLARENET")]
    [TestCase("8.8.8.8", 15169, "GOOGLE")]
    [TestCase("2001:4860:4860::8888", 15169, "GOOGLE")]
    public async Task FindAsync_ReturnsNetwork_ForAddressInsideRange(string ip, int expectedAsn, string expectedOrg)
    {
        var source = new IpToAsnFileDataSource(WritePlain());

        var lookup = (await source.FindAsync(IPAddress.Parse(ip))).Match;

        Assert.That(lookup, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(lookup!.Network.Asn, Is.EqualTo(expectedAsn));
            Assert.That(lookup.Network.Organization, Is.EqualTo(expectedOrg));
            Assert.That(lookup.Network.CountryCode, Is.EqualTo("US"));
        });
    }

    [TestCase("1.0.0.1", "1.0.0.0", "1.0.0.255")]
    [TestCase("2001:4860:4860::8888", "2001:4860::", "2001:4860:ffff:ffff:ffff:ffff:ffff:ffff")]
    public async Task FindAsync_ReturnsMatchingRange(string ip, string expectedStart, string expectedEnd)
    {
        var lookup = (await new IpToAsnFileDataSource(WritePlain()).FindAsync(IPAddress.Parse(ip))).Match;

        Assert.Multiple(() =>
        {
            Assert.That(lookup!.RangeStart, Is.EqualTo(IPAddress.Parse(expectedStart)));
            Assert.That(lookup.RangeEnd, Is.EqualTo(IPAddress.Parse(expectedEnd)));
        });
    }

    [Test]
    public async Task FindAsync_ReportsFileTimestampAsDataUpdatedAt()
    {
        var path = WritePlain();
        var timestamp = new DateTime(2026, 11, 1, 6, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, timestamp);

        var search = await new IpToAsnFileDataSource(path).FindAsync(IPAddress.Parse("8.8.8.8"));

        Assert.That(search.DataUpdatedAt, Is.EqualTo(new DateTimeOffset(timestamp)));
    }

    [TestCase("1.0.2.1")]   // unrouted (ASN 0)
    [TestCase("9.9.9.9")]   // not in dataset
    [TestCase("0.0.0.1")]   // below first range
    [TestCase("2606:4700::1111")]
    public async Task FindAsync_ReturnsNotInDataset_WhenAddressIsNotAnnounced(string ip)
    {
        var source = new IpToAsnFileDataSource(WritePlain());

        var search = await source.FindAsync(IPAddress.Parse(ip));

        Assert.Multiple(() =>
        {
            Assert.That(search.Outcome, Is.EqualTo(AsnSearchOutcome.NotInDataset));
            Assert.That(search.Match, Is.Null);
        });
    }

    [Test]
    public async Task FindAsync_ReadsGzipCompressedDataset()
    {
        var path = Path.Combine(_directory, "ip2asn-combined.tsv.gz");
        await using (var gzip = new GZipStream(File.Create(path), CompressionLevel.Fastest))
        await using (var writer = new StreamWriter(gzip))
        {
            await writer.WriteAsync(Dataset);
        }

        var outside = await new IpToAsnFileDataSource(path).FindAsync(IPAddress.Parse("8.8.4.4"));

        Assert.That(outside.Match, Is.Null, "8.8.4.4 is outside 8.8.8.0/24");
        Assert.That((await new IpToAsnFileDataSource(path).FindAsync(IPAddress.Parse("8.8.8.8"))).Match?.Network.Asn, Is.EqualTo(15169));
    }

    [Test]
    public async Task FindAsync_ReturnsDataNotAvailable_WhenFileIsMissing()
    {
        var source = new IpToAsnFileDataSource(Path.Combine(_directory, "missing.tsv"));

        var search = await source.FindAsync(IPAddress.Parse("8.8.8.8"));

        Assert.That(search.Outcome, Is.EqualTo(AsnSearchOutcome.DataNotAvailable));
    }

    [Test]
    public async Task FindAsync_RetriesLoading_AfterFileAppears()
    {
        var path = Path.Combine(_directory, "late.tsv");
        var source = new IpToAsnFileDataSource(path);
        Assert.That((await source.FindAsync(IPAddress.Parse("8.8.8.8"))).Outcome, Is.EqualTo(AsnSearchOutcome.DataNotAvailable));

        await File.WriteAllTextAsync(path, Dataset);

        Assert.That((await source.FindAsync(IPAddress.Parse("8.8.8.8"))).Match?.Network.Asn, Is.EqualTo(15169));
    }

    private string WritePlain()
    {
        var path = Path.Combine(_directory, "ip2asn-combined.tsv");
        File.WriteAllText(path, Dataset);
        return path;
    }
}
