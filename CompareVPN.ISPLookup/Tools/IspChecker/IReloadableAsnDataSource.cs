namespace CompareVPN.Tools.IspChecker;

/// <summary>A data source that reads its list of networks from a file which can be replaced while the website runs.</summary>
public interface IReloadableAsnDataSource : IAsnDataSource
{
    /// <summary>The full path of the file the list is read from.</summary>
    string FilePath { get; }

    /// <summary>True once a list has been loaded and lookups can be answered.</summary>
    bool IsLoaded { get; }

    /// <summary>When the loaded list was last refreshed, or <c>null</c> if nothing is loaded.</summary>
    DateTimeOffset? DataUpdatedAt { get; }

    /// <summary>
    /// Loads the file if it has changed (or was never loaded). Throws if the new file can't be read; the previous
    /// copy then keeps answering lookups.
    /// </summary>
    Task<DatasetReloadOutcome> ReloadIfChangedAsync(CancellationToken cancellationToken = default);
}
