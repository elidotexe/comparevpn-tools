using System.Net;

namespace CompareVPN.Tools.IspChecker.Tests;

[TestFixture]
public class DbIpCityFileDataSourceTests
{
    private string _directory = null!;
    private string _path = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("ispchecker-location-tests").FullName;
        _path = Path.Combine(_directory, "dbip-city-lite.mmdb");
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [Test]
    public async Task FindAsync_ReturnsPlace_ForIPv4AddressInFile()
    {
        WriteFile();
        using var source = await LoadedSource();

        var location = await source.FindAsync(IPAddress.Parse("8.8.8.8"));

        Assert.That(location, Is.EqualTo(new IpLocation("Mountain View", "California", "US", 37.422, -122.085)));
    }

    [Test]
    public async Task FindAsync_ReturnsPlace_ForIPv6AddressInFile()
    {
        WriteFile();
        using var source = await LoadedSource();

        var location = await source.FindAsync(IPAddress.Parse("2001:4860:4860::8888"));

        Assert.That(location, Is.EqualTo(new IpLocation("Montreal", "Quebec", "CA", 45.5019, -73.5674)));
    }

    [Test]
    public async Task FindAsync_LeavesOutWhatTheFileDoesNotKnow()
    {
        WriteFile();
        using var source = await LoadedSource();

        var location = await source.FindAsync(IPAddress.Parse("81.2.69.160"));

        Assert.That(location, Is.EqualTo(new IpLocation(null, null, "GB", null, null)));
    }

    [Test]
    public async Task FindAsync_ReturnsNull_ForAddressNotInFile()
    {
        WriteFile();
        using var source = await LoadedSource();

        Assert.That(await source.FindAsync(IPAddress.Parse("9.9.9.9")), Is.Null);
    }

    [Test]
    public async Task FindAsync_ReturnsNull_BeforeAnythingIsLoaded()
    {
        WriteFile();
        using var source = new DbIpCityFileDataSource(_path);

        Assert.Multiple(async () =>
        {
            Assert.That(source.IsLoaded, Is.False);
            Assert.That(await source.FindAsync(IPAddress.Parse("8.8.8.8")), Is.Null);
        });
    }

    [Test]
    public async Task Reload_ReportsFileMissing_WhenThereIsNoFile()
    {
        using var source = new DbIpCityFileDataSource(_path);

        Assert.Multiple(async () =>
        {
            Assert.That(await source.ReloadIfChangedAsync(), Is.EqualTo(DatasetReloadOutcome.FileMissing));
            Assert.That(source.IsLoaded, Is.False);
            Assert.That(source.DataUpdatedAt, Is.Null);
        });
    }

    [Test]
    public async Task Reload_ReportsNoChange_WhenTheFileIsTheSame()
    {
        WriteFile();
        using var source = await LoadedSource();

        Assert.That(await source.ReloadIfChangedAsync(), Is.EqualTo(DatasetReloadOutcome.NoChange));
    }

    [Test]
    public async Task Reload_SwapsInANewerFile()
    {
        WriteFile();
        using var source = await LoadedSource();

        TestLocationFile.Write(_path, ("8.8.8.0/24", TestLocationFile.Place("Ashburn", "Virginia", "US", 39.04, -77.49)));
        File.SetLastWriteTimeUtc(_path, DateTime.UtcNow.AddMinutes(1));
        var outcome = await source.ReloadIfChangedAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(outcome, Is.EqualTo(DatasetReloadOutcome.Loaded));
            Assert.That((await source.FindAsync(IPAddress.Parse("8.8.8.8")))?.City, Is.EqualTo("Ashburn"));
        });
    }

    [Test]
    public async Task Reload_KeepsTheOldCopy_WhenTheNewFileIsBroken()
    {
        WriteFile();
        using var source = await LoadedSource();

        await File.WriteAllTextAsync(_path, "not a location file");
        File.SetLastWriteTimeUtc(_path, DateTime.UtcNow.AddMinutes(1));

        Assert.Multiple(async () =>
        {
            Assert.That(() => source.ReloadIfChangedAsync(), Throws.Exception);
            Assert.That((await source.FindAsync(IPAddress.Parse("8.8.8.8")))?.City, Is.EqualTo("Mountain View"));
        });
    }

    [Test]
    public async Task Reload_KeepsTheLoadedCopy_WhenTheFileDisappears()
    {
        WriteFile();
        using var source = await LoadedSource();

        File.Delete(_path);

        Assert.Multiple(async () =>
        {
            Assert.That(await source.ReloadIfChangedAsync(), Is.EqualTo(DatasetReloadOutcome.NoChange));
            Assert.That((await source.FindAsync(IPAddress.Parse("8.8.8.8")))?.City, Is.EqualTo("Mountain View"));
        });
    }

    // ───── Helpers ─────

    private void WriteFile() => TestLocationFile.Write(
        _path,
        ("8.8.8.0/24", TestLocationFile.Place("Mountain View", "California", "US", 37.422, -122.085)),
        ("81.2.69.0/24", TestLocationFile.Place(null, null, "GB", null, null)),
        ("2001:4860::/32", TestLocationFile.Place("Montreal", "Quebec", "CA", 45.5019, -73.5674)));

    private async Task<DbIpCityFileDataSource> LoadedSource()
    {
        var source = new DbIpCityFileDataSource(_path);
        Assert.That(await source.ReloadIfChangedAsync(), Is.EqualTo(DatasetReloadOutcome.Loaded));
        return source;
    }
}
