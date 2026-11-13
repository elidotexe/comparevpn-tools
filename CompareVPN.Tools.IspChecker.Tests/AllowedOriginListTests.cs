namespace CompareVPN.Tools.IspChecker.Tests;

[TestFixture]
public class AllowedOriginListTests
{
    [Test]
    public void TryParse_NormalisesOriginsTheWayBrowsersSendThem()
    {
        var ok = AllowedOriginList.TryParse(
            ["https://CompareVPN.com/", "http://localhost:3000, https://*.comparevpn.com", "https://example.com:443", "https://comparevpn.com"],
            out var origins, out var invalid);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True, invalid);
            Assert.That(origins, Is.EqualTo(new[]
            {
                "https://comparevpn.com",
                "http://localhost:3000",
                "https://*.comparevpn.com",
                "https://example.com",
            }));
        });
    }

    [Test]
    public void TryParse_AcceptsAnyOriginWildcard()
    {
        Assert.That(AllowedOriginList.TryParse(["*"], out var origins, out _), Is.True);
        Assert.That(origins, Is.EqualTo(new[] { "*" }));
    }

    [TestCase("comparevpn.com")]               // no scheme
    [TestCase("https://comparevpn.com/tools")] // path
    [TestCase("https://comparevpn.com?x=1")]
    [TestCase("ftp://comparevpn.com")]
    [TestCase("https://user@comparevpn.com")]
    public void TryParse_RejectsAnythingThatIsNotABareOrigin(string entry)
    {
        var ok = AllowedOriginList.TryParse([entry], out _, out var invalid);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(invalid, Is.EqualTo(entry));
        });
    }
}
