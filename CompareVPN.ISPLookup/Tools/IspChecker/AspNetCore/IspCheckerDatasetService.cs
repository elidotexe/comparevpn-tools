using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompareVPN.Tools.IspChecker.AspNetCore;

/// <summary>
/// This class keeps the ISP checker's list of networks up to date while the website runs, like someone who checks
/// the mailbox every minute for a newer phone book.
///
/// In plain terms, here is what happens:
///   1. When the website starts, it loads the list in the background, so start-up is never held up and the first
///      visitor doesn't have to wait for it.
///   2. Every minute (the <c>ReloadCheckInterval</c> setting) it checks whether the file has changed, for example
///      after the daily download, and loads the new copy if it has.
///   3. It writes a note in the logs when a new copy is loaded, or when something is wrong.
///   Problems never stop the website: the old list keeps being used, each problem is noted once rather than every
///   minute, and it simply tries again at the next check.
/// </summary>
internal sealed class IspCheckerDatasetService(
    IAsnDataSource dataSource,
    IOptions<IspCheckerOptions> options,
    TimeProvider timeProvider,
    ILogger<IspCheckerDatasetService> logger) : BackgroundService
{
    // The last problem written to the logs, so the same problem isn't repeated at every check.
    private string? _lastProblem;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A data source that isn't a reloadable file looks after its own loading.
        if (dataSource is not IReloadableAsnDataSource file)
        {
            return;
        }

        do
        {
            // Step 1 (first time round) and step 2: load the file, or a newer copy of it.
            if (!await CheckFileAsync(file, stoppingToken))
            {
                return;
            }
        }
        while (await WaitForNextCheckAsync(stoppingToken));
    }

    // ───── Helpers for checking the file (steps 1–3) ─────

    /// <summary>Checks the file once and notes what happened. Returns false when the website is shutting down.</summary>
    private async Task<bool> CheckFileAsync(IReloadableAsnDataSource file, CancellationToken stoppingToken)
    {
        try
        {
            NoteOutcome(file, await file.ReloadIfChangedAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            // This runs outside any visitor's request, so nothing else would log it. The website keeps the list it
            // already has, and the next check tries again.
            WarnOnce(exception.Message, () => logger.LogWarning(
                exception,
                file.IsLoaded
                    ? "ISP dataset could not be reloaded; still serving the data from {DataUpdatedAt:u}"
                    : "ISP dataset could not be loaded; lookups return 503 until it is available",
                file.DataUpdatedAt));
        }

        return true;
    }

    /// <summary>Step 3: Writes a note in the logs about what the check found.</summary>
    private void NoteOutcome(IReloadableAsnDataSource file, DatasetReloadOutcome outcome)
    {
        switch (outcome)
        {
            case DatasetReloadOutcome.Loaded:
                logger.LogInformation("ISP dataset loaded (data updated {DataUpdatedAt:u})", file.DataUpdatedAt);
                _lastProblem = null;
                break;
            case DatasetReloadOutcome.NoChange:
                _lastProblem = null;
                break;
            case DatasetReloadOutcome.FileMissing:
                WarnOnce("file missing", () => logger.LogWarning(
                    "ISP dataset not found at {FilePath}; lookups return 503 until it is available. " +
                    "Download it with scripts/download-data.sh.",
                    file.FilePath));
                break;
            default:
                throw new InvalidOperationException($"Unexpected dataset reload outcome: {outcome}");
        }
    }

    /// <summary>Writes a warning, unless the same problem was already noted at the previous check.</summary>
    private void WarnOnce(string problem, Action writeWarning)
    {
        if (problem == _lastProblem)
        {
            return;
        }

        writeWarning();
        _lastProblem = problem;
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
