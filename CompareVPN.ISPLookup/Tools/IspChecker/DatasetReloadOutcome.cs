namespace CompareVPN.Tools.IspChecker;

/// <summary>What happened when a data source checked its file for a newer copy.</summary>
public enum DatasetReloadOutcome
{
    /// <summary>A new copy was loaded and is now used for lookups.</summary>
    Loaded,

    /// <summary>Nothing to do: the file hasn't changed, or it disappeared but a loaded copy is still in use.</summary>
    NoChange,

    /// <summary>The file isn't there and nothing is loaded yet, so lookups can't be answered.</summary>
    FileMissing,
}
