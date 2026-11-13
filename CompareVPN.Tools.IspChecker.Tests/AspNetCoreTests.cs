using System.Net;
using CompareVPN.Tools.IspChecker.AspNetCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Moq;

namespace CompareVPN.Tools.IspChecker.Tests;

[TestFixture]
public class IspCheckerHealthCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Unhealthy_WhenDatasetIsNotLoaded()
    {
        var result = await Check(isLoaded: false, updatedAt: null);

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Unhealthy));
    }

    [Test]
    public async Task Healthy_WhenDatasetIsLoadedAndFresh()
    {
        var result = await Check(isLoaded: true, updatedAt: Now.AddHours(-20));

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
    }

    [Test]
    public async Task Degraded_WhenDatasetIsOlderThanMaxDataAge()
    {
        var result = await Check(isLoaded: true, updatedAt: Now.AddDays(-4));

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Degraded));
    }

    [Test]
    public async Task Healthy_ForCustomDataSource()
    {
        var check = new IspCheckerHealthCheck(Mock.Of<IAsnDataSource>(), Options.Create(new IspCheckerOptions()), new FixedTime(Now));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
    }

    private static Task<HealthCheckResult> Check(bool isLoaded, DateTimeOffset? updatedAt)
    {
        var source = new Mock<IReloadableAsnDataSource>();
        source.SetupGet(s => s.IsLoaded).Returns(isLoaded);
        source.SetupGet(s => s.DataUpdatedAt).Returns(updatedAt);
        var options = Options.Create(new IspCheckerOptions { MaxDataAge = TimeSpan.FromDays(3) });

        return new IspCheckerHealthCheck(source.Object, options, new FixedTime(Now)).CheckHealthAsync(new HealthCheckContext());
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

[TestFixture]
public class IspCheckerOptionsValidatorTests
{
    [Test]
    public void Succeeds_ForDefaults()
    {
        Assert.That(new IspCheckerOptionsValidator().Validate(null, new IspCheckerOptions()).Succeeded, Is.True);
    }

    [Test]
    public void Fails_WithOffendingEntry_ForInvalidTrustedProxy()
    {
        var result = new IspCheckerOptionsValidator().Validate(null, new IspCheckerOptions { TrustedProxies = ["172.18.0.0/16", "nginx"] });

        Assert.Multiple(() =>
        {
            Assert.That(result.Failed, Is.True);
            Assert.That(result.FailureMessage, Does.Contain("'nginx'"));
        });
    }

    [TestCase("")]
    [TestCase("   ")]
    public void Fails_WhenDataFilePathIsEmpty(string path)
    {
        var result = new IspCheckerOptionsValidator().Validate(null, new IspCheckerOptions { DataFilePath = path });

        Assert.That(result.FailureMessage, Does.Contain("DataFilePath must be set"));
    }

    [Test]
    public void Fails_WithOffendingEntry_ForInvalidAllowedOrigin()
    {
        var result = new IspCheckerOptionsValidator().Validate(null, new IspCheckerOptions { AllowedOrigins = ["https://comparevpn.co/tools"] });

        Assert.That(result.FailureMessage, Does.Contain("'https://comparevpn.co/tools'"));
    }

    [Test]
    public void Fails_WhenLookupLimitIsBelowOne()
    {
        var result = new IspCheckerOptionsValidator().Validate(null, new IspCheckerOptions { LookupRequestsPerMinute = 0 });

        Assert.That(result.FailureMessage, Does.Contain("LookupRequestsPerMinute must be at least 1"));
    }

    [Test]
    public void ReportsEveryProblem_AtOnce()
    {
        var result = new IspCheckerOptionsValidator().Validate(
            null, new IspCheckerOptions { DataFilePath = "", LookupRequestsPerMinute = 0 });

        Assert.That(result.Failures?.Count(), Is.EqualTo(2));
    }

    [Test]
    public void Fails_ForNegativeReloadInterval()
    {
        var result = new IspCheckerOptionsValidator().Validate(null, new IspCheckerOptions { ReloadCheckInterval = TimeSpan.FromSeconds(-1) });

        Assert.That(result.Failed, Is.True);
    }
}
