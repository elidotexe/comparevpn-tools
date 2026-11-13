using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace CompareVPN.Tools.IspChecker.AspNetCore;

/// <summary>
/// This class answers <c>/healthz</c>, which servers and monitoring tools ask to find out whether the ISP checker can
/// do its job.
///
/// In plain terms, here is what happens:
///   1. If nothing is loaded yet, it answers "Unhealthy" (status 503), so a server can hold back visitors until
///      lookups work.
///   2. If the loaded list is older than the <c>MaxDataAge</c> setting, it answers "Degraded" (still status 200):
///      lookups work, but the daily download has probably stopped.
///   3. Otherwise it answers "Healthy".
///   A self-hoster's own data source, which isn't a reloadable file, always counts as healthy.
/// </summary>
internal sealed class IspCheckerHealthCheck(IAsnDataSource dataSource, IOptions<IspCheckerOptions> options, TimeProvider timeProvider)
    : IHealthCheck
{
    /// <summary>Returns how healthy the ISP checker's data is, with the data's date when it is loaded.</summary>
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(Check());

    private HealthCheckResult Check()
    {
        if (dataSource is not IReloadableAsnDataSource file)
        {
            return HealthCheckResult.Healthy("Custom ISP data source.");
        }

        // Step 1: Nothing loaded yet.
        if (!file.IsLoaded || file.DataUpdatedAt is not { } updatedAt)
        {
            return HealthCheckResult.Unhealthy("ISP dataset is not loaded.");
        }

        // Step 2 and 3: Loaded, but is it fresh enough?
        var data = new Dictionary<string, object> { ["dataUpdatedAt"] = updatedAt };
        var age = timeProvider.GetUtcNow() - updatedAt;

        return age > options.Value.MaxDataAge
            ? HealthCheckResult.Degraded($"ISP dataset is {age.TotalDays:0.#} days old.", data: data)
            : HealthCheckResult.Healthy("ISP dataset is loaded.", data);
    }
}
