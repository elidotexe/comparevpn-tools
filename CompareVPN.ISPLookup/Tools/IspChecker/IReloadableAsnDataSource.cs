namespace CompareVPN.Tools.IspChecker;

/// <summary>A data source backed by a file that can be refreshed while the app runs.</summary>
public interface IReloadableAsnDataSource : IAsnDataSource
{
    /// <summary>True once a dataset has been loaded and lookups can be answered.</summary>
    bool IsLoaded { get; }

    /// <summary>When the loaded dataset was last refreshed, or null if nothing is loaded.</summary>
    DateTimeOffset? DataUpdatedAt { get; }

    /// <summary>
    /// Loads the dataset if it has changed (or was never loaded). Returns true if a new copy was swapped in.
    /// Throws if the new file cannot be loaded; the previous copy keeps serving lookups.
    /// </summary>
    Task<bool> ReloadIfChangedAsync(CancellationToken cancellationToken = default);
}
