using System.Net;

namespace CompareVPN.Tools.IspChecker.Tests;

[TestFixture]
public class TrustedProxyListTests
{
    [Test]
    public void TryParse_AcceptsCidrsAddressesAndCommaSeparatedEntries()
    {
        var ok = TrustedProxyList.TryParse(["172.18.0.0/16", "10.0.0.5, fd00::/8", " 2001:db8::1 "], out var networks, out var invalid);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.True);
            Assert.That(invalid, Is.Null);
            Assert.That(networks, Is.EqualTo(new[]
            {
                IPNetwork.Parse("172.18.0.0/16"),
                IPNetwork.Parse("10.0.0.5/32"),
                IPNetwork.Parse("fd00::/8"),
                IPNetwork.Parse("2001:db8::1/128"),
            }));
        });
    }

    [Test]
    public void TryParse_AcceptsEmptyList()
    {
        Assert.That(TrustedProxyList.TryParse([], out var networks, out _), Is.True);
        Assert.That(networks, Is.Empty);
    }

    [TestCase("172.18.0.1/16")]  // host bits set: probably meant 172.18.0.0/16
    [TestCase("172.18.0.0/33")]
    [TestCase("nginx")]          // hostnames are not resolved
    [TestCase("10.0")]
    public void TryParse_RejectsInvalidEntry(string entry)
    {
        var ok = TrustedProxyList.TryParse(["10.0.0.0/8", entry], out _, out var invalid);

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(invalid, Is.EqualTo(entry));
        });
    }
}
