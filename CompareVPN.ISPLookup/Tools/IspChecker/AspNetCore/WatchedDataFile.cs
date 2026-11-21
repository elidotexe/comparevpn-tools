using CompareVPN.Data;

namespace CompareVPN.Tools.IspChecker.AspNetCore;

/// <summary>
/// One data file the background reload job keeps fresh, with the words it uses about that file in the logs.
/// </summary>
/// <param name="File">The file and the data loaded from it.</param>
/// <param name="Name">What the logs call it, e.g. "ISP dataset".</param>
/// <param name="WhenUnavailable">What visitors notice while nothing is loaded, e.g. "lookups return 503 until it is available".</param>
internal sealed class WatchedDataFile(IReloadableDataFile file, string name, string whenUnavailable)
{
    public IReloadableDataFile File => file;

    public string Name => name;

    public string WhenUnavailable => whenUnavailable;

    /// <summary>The last problem written to the logs, so the same problem isn't repeated at every check.</summary>
    public string? LastProblem { get; set; }
}
