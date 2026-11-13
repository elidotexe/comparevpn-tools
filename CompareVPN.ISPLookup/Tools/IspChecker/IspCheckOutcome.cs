namespace CompareVPN.Tools.IspChecker;

/// <summary>Whether the ISP checker could answer.</summary>
public enum IspCheckOutcome
{
    /// <summary>The address was checked; the result says what was found (which may be "no provider").</summary>
    Checked,

    /// <summary>No list of networks is loaded yet, so the address couldn't be checked. Try again later.</summary>
    DataNotAvailable,
}
