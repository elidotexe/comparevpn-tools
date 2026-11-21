using System.Net;
using CompareVPN.Data.Networks;
using CompareVPN.Data;

namespace CompareVPN.Tools.IspChecker.Tests;

[TestFixture]
public class DatasetReloadTests
{
    private const string OldData = "8.8.8.0\t8.8.8.255\t15169\tUS\tGOOGLE\n";
    private const string NewData = "8.8.8.0\t8.8.8.255\t64512\tDE\tNEW-OWNER\n";

    private string _directory = null!;
    private string _path = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("ispchecker-reload").FullName;
        _path = Path.Combine(_directory, "ip2asn-combined.tsv");
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [Test]
    public async Task ReloadIfChanged_SwapsInNewerFile()
    {
        Write(OldData, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
        var source = new IpToAsnFileDataSource(_path);
        Assert.That((await source.FindAsync(IPAddress.Parse("8.8.8.8"))).Match!.Network.Asn, Is.EqualTo(15169));

        Write(NewData, new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc));

        Assert.That(await source.ReloadIfChangedAsync(), Is.EqualTo(DatasetReloadOutcome.Loaded));
        var lookup = (await source.FindAsync(IPAddress.Parse("8.8.8.8"))).Match;
        Assert.Multiple(() =>
        {
            Assert.That(lookup!.Network.Asn, Is.EqualTo(64512));
            Assert.That(source.DataUpdatedAt, Is.EqualTo(new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero)));
        });
    }

    [Test]
    public async Task ReloadIfChanged_DoesNothing_WhenFileIsUnchanged()
    {
        Write(OldData, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
        var source = new IpToAsnFileDataSource(_path);
        await source.ReloadIfChangedAsync(); // initial load

        Assert.That(await source.ReloadIfChangedAsync(), Is.EqualTo(DatasetReloadOutcome.NoChange));
    }

    [Test]
    public async Task ReloadIfChanged_KeepsServingOldData_WhenNewFileIsBroken()
    {
        Write(OldData, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
        var source = new IpToAsnFileDataSource(_path);
        await source.ReloadIfChangedAsync();

        Write("garbage\nnot a dataset\n", new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc));

        Assert.ThrowsAsync<InvalidDataException>(() => source.ReloadIfChangedAsync());
        Assert.Multiple(async () =>
        {
            Assert.That((await source.FindAsync(IPAddress.Parse("8.8.8.8"))).Match!.Network.Asn, Is.EqualTo(15169));
            Assert.That(source.DataUpdatedAt, Is.EqualTo(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero)));
        });
    }

    [Test]
    public async Task ReloadIfChanged_KeepsServingOldData_WhenFileIsDeleted()
    {
        Write(OldData, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
        var source = new IpToAsnFileDataSource(_path);
        await source.ReloadIfChangedAsync();

        File.Delete(_path);

        Assert.That(await source.ReloadIfChangedAsync(), Is.EqualTo(DatasetReloadOutcome.NoChange));
        Assert.That((await source.FindAsync(IPAddress.Parse("8.8.8.8"))).Match!.Network.Asn, Is.EqualTo(15169));
    }

    [Test]
    public async Task ReloadIfChanged_LoadsFile_ThatAppearsAfterStartup()
    {
        var source = new IpToAsnFileDataSource(_path);
        Assert.That(await source.ReloadIfChangedAsync(), Is.EqualTo(DatasetReloadOutcome.FileMissing));
        Assert.That(source.IsLoaded, Is.False);

        Write(OldData, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.That(await source.ReloadIfChangedAsync(), Is.EqualTo(DatasetReloadOutcome.Loaded));
        Assert.That(source.IsLoaded, Is.True);
    }

    [Test]
    public void InitialLoad_RejectsFileWithNoValidRanges()
    {
        Write("only garbage\n", DateTime.UtcNow);

        Assert.ThrowsAsync<InvalidDataException>(async () =>
            await new IpToAsnFileDataSource(_path).FindAsync(IPAddress.Parse("8.8.8.8")));
    }

    private void Write(string content, DateTime lastWriteUtc)
    {
        File.WriteAllText(_path, content);
        File.SetLastWriteTimeUtc(_path, lastWriteUtc);
    }
}
