namespace EVBlocker.Core.History;

/// <summary>
/// Reads which executables have tried to reach the network, from the WFP audit log.
/// </summary>
public interface IAttemptHistoryReader
{
    /// <summary>
    /// Attempts within <paramref name="lookback"/>, collapsed per executable and newest first.
    /// </summary>
    /// <param name="lookback">How far back to look. Filtered in the log query, not in memory.</param>
    /// <param name="maxEvents">
    /// Upper bound on events examined. The Security log can hold hundreds of thousands of these,
    /// so an unbounded read would stall the UI; the newest events are examined first so a cap
    /// costs the oldest data, not the most relevant.
    /// </param>
    IReadOnlyList<AttemptSummary> ReadSummaries(TimeSpan lookback, int maxEvents);
}
