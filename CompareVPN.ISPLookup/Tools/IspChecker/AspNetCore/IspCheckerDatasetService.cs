using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompareVPN.Tools.IspChecker.AspNetCore;

/// <summary>
/// This class keeps the ISP checker's data files up to date while the website runs, like someone who checks the
/// mailbox every minute for a newer phone book.
///
/// In plain terms, here is what happens:
///   1. When the website starts, it loads each file in the background, so start-up is never held up and the first
///      visitor doesn't have to wait for it.
///   2. Every minute (the <c>ReloadCheckInterval</c> setting) it checks whether a file has changed, for example
///      after the daily download, and loads the new copy if it has.
///   3. It writes a note in the logs when a new copy is loaded, or when something is wrong.
///   Problems never stop the website: the old data keeps being used, each problem is noted once rather than every
///   minute, and it simply tries again at the next check.
/// </summary>
internal sealed class IspCheckerDatasetService(
    IAsnDataSource dataSource,
    IOptions<IspCheckerOptions> options,
    TimeProvider timeProvider,
    ILogger<IspCheckerDatasetService> logger) : BackgroundService
{
    // A data source that isn't a reloadable file looks after its own loading, so it isn't watched here.
    private readonly WatchedDataFile[] _files = dataSource is IReloadableAsnDataSource networks
        ? [new WatchedDataFile(networks, "ISP dataset", "lookups return 503 until it is available")]
        : [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_files.Length == 0)
        {
            return;
        }

        do
        {
            // Step 1 (first time round) and step 2: load each file, or a newer copy of it.
            foreach (var file in _files)
            {
                if (!await CheckFileAsync(file, stoppingToken))
                {
                    return;
                }
            }
        }
        while (await WaitForNextCheckAsync(stoppingToken));
    }

    // ───── Helpers for checking the files (steps 1–3) ─────

    /// <summary>Checks one file once and notes what happened. Returns false when the website is shutting down.</summary>
    private async Task<bool> CheckFileAsync(WatchedDataFile watched, CancellationToken stoppingToken)
    {
        try
        {
            NoteOutcome(watched, await watched.File.ReloadIfChangedAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            // This runs outside any visitor's request, so nothing else would log it. The website keeps the data it
            // already has, and the next check tries again.
            WarnOnce(watched, exception.Message, () =>
            {
                if (watched.File.IsLoaded)
                {
                    logger.LogWarning(
                        exception,
                        "{Dataset} could not be reloaded; still serving the data from {DataUpdatedAt:u}",
                        watched.Name,
                        watched.File.DataUpdatedAt);
                }
                else
                {
                    logger.LogWarning(exception, "{Dataset} could not be loaded; {WhenUnavailable}", watched.Name, watched.WhenUnavailable);
                }
            });
        }

        return true;
    }

    /// <summary>Step 3: Writes a note in the logs about what the check found.</summary>
    private void NoteOutcome(WatchedDataFile watched, DatasetReloadOutcome outcome)
    {
        switch (outcome)
        {
            case DatasetReloadOutcome.Loaded:
                logger.LogInformation(
                    "{Dataset} loaded (data updated {DataUpdatedAt:u})", watched.Name, watched.File.DataUpdatedAt);
                watched.LastProblem = null;
                break;
            case DatasetReloadOutcome.NoChange:
                watched.LastProblem = null;
                break;
            case DatasetReloadOutcome.FileMissing:
                WarnOnce(watched, "file missing", () => logger.LogWarning(
                    "{Dataset} not found at {FilePath}; {WhenUnavailable}. Download it with scripts/download-data.sh.",
                    watched.Name,
                    watched.File.FilePath,
                    watched.WhenUnavailable));
                break;
            default:
                throw new InvalidOperationException($"Unexpected dataset reload outcome: {outcome}");
        }
    }

    /// <summary>Writes a warning, unless the same problem with the same file was already noted at the previous check.</summary>
    private static void WarnOnce(WatchedDataFile watched, string problem, Action writeWarning)
    {
        if (problem == watched.LastProblem)
        {
            return;
        }

        writeWarning();
        watched.LastProblem = problem;
    }

    /// <summary>
    /// Step 2: Waits until the next check. Returns false when reloading is turned off or the website is shutting down.
    /// </summary>
    private async Task<bool> WaitForNextCheckAsync(CancellationToken stoppingToken)
    {
        // An interval of zero turns reloading off: the load at start-up was the only one.
        var interval = options.Value.ReloadCheckInterval;
        if (interval <= TimeSpan.Zero)
        {
            return false;
        }

        try
        {
            await Task.Delay(interval, timeProvider, stoppingToken);
            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
