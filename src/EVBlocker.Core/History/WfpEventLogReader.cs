using System.Diagnostics.Eventing.Reader;
using System.Globalization;

namespace EVBlocker.Core.History;

/// <summary>
/// Reads WFP audit events 5157 / 5156 from the Windows Security log and groups them per executable.
/// </summary>
/// <remarks>
/// Requires administrator rights: the Security log is not readable by a standard user.
/// Requires the "Filtering Platform Connection" audit subcategory to be enabled, otherwise the
/// log simply contains no such events and this returns an empty list - see AuditPolicyManager.
/// </remarks>
public sealed class WfpEventLogReader : IAttemptHistoryReader
{
    /// <summary>Distinct destinations kept per executable, to bound memory on a chatty process.</summary>
    private const int MaxSampleDestinations = 5;

    private readonly IDevicePathMapper _mapper;

    public WfpEventLogReader(IDevicePathMapper mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        _mapper = mapper;
    }

    public IReadOnlyList<AttemptSummary> ReadSummaries(TimeSpan lookback, int maxEvents)
    {
        if (lookback <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lookback), lookback, "Lookback must be positive.");
        }

        if (maxEvents <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEvents), maxEvents, "Event cap must be positive.");
        }

        var accumulators = new Dictionary<string, Accumulator>(StringComparer.OrdinalIgnoreCase);

        var query = new EventLogQuery("Security", PathType.LogName, BuildXPath(lookback))
        {
            // Newest first, so the maxEvents cap discards the oldest events rather than the newest.
            ReverseDirection = true,
        };

        using var reader = new EventLogReader(query);

        int examined = 0;
        while (examined < maxEvents)
        {
            EventRecord? record = reader.ReadEvent();
            if (record is null)
            {
                break;
            }

            using (record)
            {
                examined++;

                NetworkAttempt? attempt = WfpEventParser.TryParse(record.ToXml(), _mapper);
                if (attempt is not null)
                {
                    Accumulate(accumulators, attempt);
                }
            }
        }

        return accumulators.Values
            .Select(a => a.ToSummary())
            .OrderByDescending(s => s.LastSeen)
            .ToList();
    }

    /// <summary>
    /// Filters in the log query rather than in memory. timediff() is evaluated by the event log
    /// service, so a 24-hour window does not require reading a week of events first.
    /// </summary>
    private static string BuildXPath(TimeSpan lookback)
    {
        long milliseconds = (long)lookback.TotalMilliseconds;

        return string.Format(
            CultureInfo.InvariantCulture,
            "*[System[(EventID={0} or EventID={1}) and TimeCreated[timediff(@SystemTime) <= {2}]]]",
            WfpEventParser.BlockedEventId,
            WfpEventParser.AllowedEventId,
            milliseconds);
    }

    private static void Accumulate(Dictionary<string, Accumulator> accumulators, NetworkAttempt attempt)
    {
        if (!accumulators.TryGetValue(attempt.ExecutablePath, out Accumulator? accumulator))
        {
            accumulator = new Accumulator(attempt.ExecutablePath);
            accumulators[attempt.ExecutablePath] = accumulator;
        }

        accumulator.Add(attempt);
    }

    /// <summary>Mutable per-executable tally, collapsed into an <see cref="AttemptSummary"/> at the end.</summary>
    private sealed class Accumulator
    {
        private readonly string _executablePath;
        private readonly HashSet<string> _destinations = new(StringComparer.OrdinalIgnoreCase);

        private int _blocked;
        private int _allowed;
        private DateTimeOffset _lastSeen;

        public Accumulator(string executablePath) => _executablePath = executablePath;

        public void Add(NetworkAttempt attempt)
        {
            if (attempt.WasBlocked)
            {
                _blocked++;
            }
            else
            {
                _allowed++;
            }

            if (attempt.Timestamp > _lastSeen)
            {
                _lastSeen = attempt.Timestamp;
            }

            if (_destinations.Count < MaxSampleDestinations && attempt.DestinationAddress is not null)
            {
                _destinations.Add(attempt.DestinationPort is int port
                    ? $"{attempt.DestinationAddress}:{port}"
                    : attempt.DestinationAddress.ToString());
            }
        }

        public AttemptSummary ToSummary() => new()
        {
            ExecutablePath = _executablePath,
            BlockedCount = _blocked,
            AllowedCount = _allowed,
            LastSeen = _lastSeen,
            SampleDestinations = _destinations.ToList(),
        };
    }
}
