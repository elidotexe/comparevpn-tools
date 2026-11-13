using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace CompareVPN.Tools.IspChecker.AspNetCore;

/// <summary>
/// Healthy once the dataset is loaded; Unhealthy (HTTP 503) before that, so orchestrators hold traffic
/// until lookups work; Degraded (HTTP 200) when the data is older than <see cref="IspCheckerOptions.MaxDataAge"/>.
/// </summary>
internal sealed class IspCheckerHealthCheck(IAsnDataSource dataSource, IOptions<IspCheckerOptions> options, TimeProvider timeProvider)
    : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (dataSource is not IReloadableAsnDataSource reloadable)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Custom ISP data source."));
        }

        if (!reloadable.IsLoaded || reloadable.DataUpdatedAt is not { } updatedAt)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("ISP dataset is not loaded."));
        }

        var data = new Dictionary<string, object> { ["dataUpdatedAt"] = updatedAt };
        var age = timeProvider.GetUtcNow() - updatedAt;

        return Task.FromResult(age > options.Value.MaxDataAge
            ? HealthCheckResult.Degraded($"ISP dataset is {age.TotalDays:0.#} days old.", data: data)
            : HealthCheckResult.Healthy("ISP dataset is loaded.", data));
    }
}
