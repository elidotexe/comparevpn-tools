using CompareVPN.Tools.IspChecker.AspNetCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompareVPN.Tools.IspChecker.Tests;

[TestFixture]
public class IspCheckerDatasetServiceTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("ispchecker-service-tests").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [Test]
    public async Task WarnsWithThePath_WhenDatasetIsMissingAtStartup()
    {
        var path = Path.Combine(_directory, "missing.tsv");
        var logger = new RecordingLogger();

        await RunOnce(new IpToAsnFileDataSource(path), logger);

        Assert.Multiple(() =>
        {
            Assert.That(logger.Entries, Has.Count.EqualTo(1));
            Assert.That(logger.Entries[0].Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(logger.Entries[0].Text, Does.Contain(path));
        });
    }

    [Test]
    public async Task LogsInformation_WhenDatasetLoads()
    {
        var path = Path.Combine(_directory, "ip2asn-combined.tsv");
        await File.WriteAllTextAsync(path, "8.8.8.0\t8.8.8.255\t15169\tUS\tGOOGLE\n");
        var logger = new RecordingLogger();
        var source = new IpToAsnFileDataSource(path);

        await RunOnce(source, logger);

        Assert.Multiple(() =>
        {
            Assert.That(source.IsLoaded, Is.True);
            Assert.That(logger.Entries.Select(e => e.Level), Is.EqualTo(new[] { LogLevel.Information }));
        });
    }

    [Test]
    public async Task WarnsWithoutStopping_WhenDatasetIsBroken()
    {
        var path = Path.Combine(_directory, "ip2asn-combined.tsv");
        await File.WriteAllTextAsync(path, "not a dataset\n");
        var logger = new RecordingLogger();

        await RunOnce(new IpToAsnFileDataSource(path), logger);

        Assert.That(logger.Entries.Select(e => e.Level), Is.EqualTo(new[] { LogLevel.Warning }));
    }

    [Test]
    public async Task WarnsWithThePath_WhenLocationFileIsMissing_AndStillLoadsTheNetworks()
    {
        var networksPath = Path.Combine(_directory, "ip2asn-combined.tsv");
        await File.WriteAllTextAsync(networksPath, "8.8.8.0\t8.8.8.255\t15169\tUS\tGOOGLE\n");
        var locationPath = Path.Combine(_directory, "missing.mmdb");
        var logger = new RecordingLogger();
        var networks = new IpToAsnFileDataSource(networksPath);
        using var locations = new DbIpCityFileDataSource(locationPath);

        await RunOnce(networks, logger, locations);

        Assert.Multiple(() =>
        {
            Assert.That(networks.IsLoaded, Is.True);
            Assert.That(logger.Entries.Select(e => e.Level), Is.EqualTo(new[] { LogLevel.Information, LogLevel.Warning }));
            Assert.That(logger.Entries[1].Text, Does.Contain(locationPath).And.Contain("without a location"));
        });
    }

    [Test]
    public async Task LoadsTheLocationFile()
    {
        var networksPath = Path.Combine(_directory, "ip2asn-combined.tsv");
        await File.WriteAllTextAsync(networksPath, "8.8.8.0\t8.8.8.255\t15169\tUS\tGOOGLE\n");
        var locationPath = Path.Combine(_directory, "dbip-city-lite.mmdb");
        TestLocationFile.Write(locationPath, ("8.8.8.0/24", TestLocationFile.Place("Mountain View", "California", "US", 37.422, -122.085)));
        var logger = new RecordingLogger();
        using var locations = new DbIpCityFileDataSource(locationPath);

        await RunOnce(new IpToAsnFileDataSource(networksPath), logger, locations);

        Assert.Multiple(() =>
        {
            Assert.That(locations.IsLoaded, Is.True);
            Assert.That(logger.Entries.Select(e => e.Level), Is.EqualTo(new[] { LogLevel.Information, LogLevel.Information }));
        });
    }

    /// <summary>Runs the service with reloading turned off, so it makes exactly one attempt and stops.</summary>
    private static async Task RunOnce(IAsnDataSource source, RecordingLogger logger, ILocationDataSource? locations = null)
    {
        var options = Options.Create(new IspCheckerOptions { ReloadCheckInterval = TimeSpan.Zero });
        using var service = new IspCheckerDatasetService(source, locations ?? new NoLocationData(), options, TimeProvider.System, logger);

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!;
    }

    private sealed class RecordingLogger : ILogger<IspCheckerDatasetService>
    {
        public List<(LogLevel Level, string Text)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception) + " " + exception));
    }
}
