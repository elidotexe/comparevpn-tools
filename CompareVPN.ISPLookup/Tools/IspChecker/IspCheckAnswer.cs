namespace CompareVPN.Tools.IspChecker;

/// <summary>The ISP checker's answer: whether it could check the address, and the result when it could.</summary>
/// <param name="Outcome">Whether the address was checked.</param>
/// <param name="Result">What was found; only set when <paramref name="Outcome"/> is <see cref="IspCheckOutcome.Checked"/>.</param>
public sealed record IspCheckAnswer(IspCheckOutcome Outcome, IspCheckResult? Result)
{
    /// <summary>The address was checked and <paramref name="result"/> describes it.</summary>
    public static IspCheckAnswer Checked(IspCheckResult result) => new(IspCheckOutcome.Checked, result);

    /// <summary>No list of networks is loaded, so the address couldn't be checked.</summary>
    public static IspCheckAnswer DataNotAvailable() => new(IspCheckOutcome.DataNotAvailable, null);
}
