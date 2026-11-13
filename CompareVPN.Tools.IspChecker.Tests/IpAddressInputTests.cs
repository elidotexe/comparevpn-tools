using System.Net;

namespace CompareVPN.Tools.IspChecker.Tests;

[TestFixture]
public class IpAddressInputTests
{
    [TestCase("8.8.8.8")]
    [TestCase(" 8.8.8.8 ")]
    [TestCase("2001:4860:4860::8888")]
    [TestCase("2001:4860:4860:0:0:0:0:8888")]
    [TestCase("::ffff:1.1.1.1")]
    public void TryParse_AcceptsStandardForms(string input)
    {
        Assert.That(IpAddressInput.TryParse(input, out var address), Is.True);
        Assert.That(address, Is.EqualTo(IPAddress.Parse(input.Trim())));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("8.8")]          // IPAddress.TryParse → 8.0.0.8
    [TestCase("134744072")]    // IPAddress.TryParse → 8.8.8.8
    [TestCase("08.8.8.8")]
    [TestCase("8.8.8.256")]
    [TestCase("fe80::1%eth0")]
    [TestCase("example.com")]
    [TestCase("8.8.8.8/24")]
    public void TryParse_RejectsAmbiguousOrInvalidInput(string? input)
    {
        Assert.That(IpAddressInput.TryParse(input, out _), Is.False);
    }
}
