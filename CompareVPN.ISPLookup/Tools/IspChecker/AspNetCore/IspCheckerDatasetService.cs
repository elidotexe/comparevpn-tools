using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompareVPN.Tools.IspChecker.AspNetCore;

/// <summary>
/// Loads the dataset in the background at startup so the first visitor doesn't wait for it, then
/// periodically reloads it when the file changes (e.g. after a daily <c>download-data.sh</c>).
/// Startup is never blocked or failed by this; problems are logged and retried.
/// </summary>
internal sealed class IspCheckerDatasetService(
    IAsnDataSource dataSource,
    IOptions<IspCheckerOptions> options,
    TimeProvider timeProvider,
    ILogger<IspCheckerDatasetService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (dataSource is not IReloadableAsnDataSource reloadable)
        {
            return; // A custom data source manages its own loading.
        }

        var interval = options.Value.ReloadCheckInterval;
        var lastFailure = (string?)null;

        do
        {
            try
            {
                if (await reloadable.ReloadIfChangedAsync(stoppingToken))
                {
                    logger.LogInformation("ISP dataset loaded (data updated {DataUpdatedAt:u})", reloadable.DataUpdatedAt);
                }

                lastFailure = null;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Log each distinct problem once rather than every interval.
                if (ex.Message != lastFailure)
                {
                    logger.LogWarning(ex, reloadable.IsLoaded
                        ? "ISP dataset could not be reloaded; still serving the data from {DataUpdatedAt:u}"
                        : "ISP dataset could not be loaded; lookups return 503 until it is available",
                        reloadable.DataUpdatedAt);
                    lastFailure = ex.Message;
                }
            }

            if (interval <= TimeSpan.Zero)
            {
                return; // Reloading disabled: the startup load was the only one.
            }

            try
            {
                await Task.Delay(interval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
        while (!stoppingToken.IsCancellationRequested);
    }
}
