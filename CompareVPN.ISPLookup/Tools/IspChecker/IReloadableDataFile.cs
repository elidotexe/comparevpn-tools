namespace CompareVPN.Tools.IspChecker;

/// <summary>
/// A downloaded data file that can be replaced while the website runs. The background reload job checks each one
/// for a newer copy and loads it without a restart.
/// </summary>
public interface IReloadableDataFile
{
    /// <summary>The full path of the file the data is read from.</summary>
    string FilePath { get; }

    /// <summary>True once the data has been loaded and lookups can be answered.</summary>
    bool IsLoaded { get; }

    /// <summary>When the loaded data was last refreshed, or <c>null</c> if nothing is loaded.</summary>
    DateTimeOffset? DataUpdatedAt { get; }

    /// <summary>
    /// Loads the file if it has changed (or was never loaded). Throws if the new file can't be read; the previous
    /// copy then keeps answering lookups.
    /// </summary>
    Task<DatasetReloadOutcome> ReloadIfChangedAsync(CancellationToken cancellationToken = default);
}
