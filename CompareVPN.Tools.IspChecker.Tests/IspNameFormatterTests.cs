using CompareVPN.Data.Networks;

namespace CompareVPN.Tools.IspChecker.Tests;

[TestFixture]
public class IspNameFormatterTests
{
    // Real descriptions from the iptoasn.com dataset.
    [TestCase("CLOUDFLARENET - Cloudflare, Inc.", "Cloudflare")]
    [TestCase("GOOGLE-CLOUD-PLATFORM - Google LLC", "Google")]
    [TestCase("MICROSOFT-CORP-MSN-AS-BLOCK - Microsoft Corporation", "Microsoft")]
    [TestCase("DTAG Deutsche Telekom AG", "Deutsche Telekom")]
    [TestCase("AS3215 Orange S.A.", "Orange")]
    [TestCase("NTL Virgin Media Limited", "Virgin Media")]
    [TestCase("HETZNER-AS Hetzner Online GmbH", "Hetzner Online")]
    [TestCase("OVH OVH SAS", "OVH")]
    [TestCase("Clouvider Clouvider Limited", "Clouvider")]
    [TestCase("M247 M247 Europe SRL", "M247 Europe")]
    [TestCase("CDN77 Datacamp Limited", "Datacamp")]
    [TestCase("LEASEWEB-NL-AMS-01 LeaseWeb Netherlands B.V.", "LeaseWeb Netherlands")]
    [TestCase("MECHA - Suzuka Cable Co.,Ltd", "Suzuka Cable")]
    [TestCase("SKB-DONGDAEMUNCABLEINTERNET-AS-KR-KR - SK Broadband Co Ltd", "SK Broadband")]
    [TestCase("Verizon Nederland B.V.", "Verizon Nederland")]
    [TestCase("PULMAN-EDU-AS Institute of Soil Science and Plant Cultivation - State Research Institute",
        "Institute of Soil Science and Plant Cultivation - State Research Institute")]
    [TestCase("Emdadkhodro-Iran-AS Emdadkhodro Iran", "Emdadkhodro Iran")]
    [TestCase("serviceplan-as Serviceplan Group SE & Co. KG", "Serviceplan Group")]
    [TestCase("netplans-cloud NetPlans GmbH", "NetPlans")]
    [TestCase("AS53076 - INTERPIRA INTERNET SERVICE PROVIDER LTDA", "Interpira Internet Service Provider")]
    [TestCase("FLEXNETWORK-AS FLEX NETWORK SARL", "Flex Network")]
    [TestCase("BELLMOBILITY-1 - BELL MOBILITY INC.", "Bell Mobility")]
    [TestCase("IMSNET-AS-ID - PT INTER MEDIALINK SOLUSI", "PT Inter Medialink Solusi")]
    [TestCase("TDMGROUP TDM Group Limited", "TDM Group")]
    [TestCase("ProtonVPN Proton AG", "Proton")]
    [TestCase("MULLVAD-AS Mullvad VPN AB", "Mullvad VPN")]
    [TestCase("ivpn IVPN Limited", "IVPN")]
    public void Format_StripsHandlesAndLegalSuffixes(string organization, string expected)
    {
        Assert.That(IspNameFormatter.Format(organization), Is.EqualTo(expected));
    }

    [TestCase("COMCAST-7922 - Comcast Cable Communications, LLC", "Comcast Xfinity")]
    [TestCase("TWC-11427-TEXAS - Charter Communications Inc", "Spectrum (Charter)")]
    [TestCase("CHARTER-20115 - Charter Communications LLC", "Spectrum (Charter)")]
    [TestCase("ATT-INTERNET4 - AT&T Enterprises, LLC", "AT&T")]
    [TestCase("UUNET - Verizon Business", "Verizon")]
    [TestCase("BT-UK-AS British Telecommunications Limited", "BT")]
    [TestCase("AS-VULTR - The Constant Company, LLC", "Vultr")]
    public void Format_UsesConsumerBrand_ForKnownOperators(string organization, string expected)
    {
        Assert.That(IspNameFormatter.Format(organization), Is.EqualTo(expected));
    }

    [TestCase("GOOGLE")]
    [TestCase("-Reserved AS-")]
    [TestCase("BSITC-AS-KR-KR - BSITC")]
    public void Format_KeepsNamesItCannotImprove(string organization)
    {
        var expected = organization.Contains(" - ") ? organization[(organization.IndexOf(" - ") + 3)..] : organization;
        Assert.That(IspNameFormatter.Format(organization), Is.EqualTo(expected));
    }

    [Test]
    public void Format_NeverReturnsEmpty_WhenNameIsOnlyALegalForm()
    {
        Assert.That(IspNameFormatter.Format("X - Limited"), Is.Not.Empty);
    }
}
