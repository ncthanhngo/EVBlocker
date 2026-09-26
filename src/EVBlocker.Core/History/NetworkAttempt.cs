using System.Net;
using EVBlocker.Core.Monitor;

namespace EVBlocker.Core.History;

/// <summary>
/// One outbound connection attempt recorded by the Windows Filtering Platform audit log.
/// </summary>
/// <remarks>
/// Covers both outcomes rather than blocked-only: event 5157 is a block, 5156 is an allow, and
/// the two share an identical shape. Keeping one type lets the UI show "tried and was blocked"
/// and "tried and got through" from the same pipeline.
///
/// Unlike a live scan, these events do carry a remote address for UDP, so this is the only view
/// of where QUIC / HTTP3 and DNS traffic actually went.
/// </remarks>
public sealed record NetworkAttempt
{
    /// <summary>Drive-letter path, translated from the kernel device path in the event.</summary>
    public required string ExecutablePath { get; init; }

    /// <summary>
    /// The path exactly as the event reported it. Kept for diagnostics: when translation fails,
    /// this is the only way to tell why a row did not match the allow-list.
    /// </summary>
    public required string RawApplication { get; init; }

    public required int ProcessId { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>True for event 5157 (blocked), false for 5156 (allowed).</summary>
    public required bool WasBlocked { get; init; }

    public IPAddress? DestinationAddress { get; init; }

    public int? DestinationPort { get; init; }

    /// <summary>Null when the event carried a protocol this app does not model (ICMP, GRE, ...).</summary>
    public TransportProtocol? Protocol { get; init; }
}

/// <summary>
/// Attempts for one executable, collapsed for display. The raw log holds thousands of rows per
/// hour, so the UI works from these rather than from individual events.
/// </summary>
public sealed record AttemptSummary
{
    public required string ExecutablePath { get; init; }
    public required int BlockedCount { get; init; }
    public required int AllowedCount { get; init; }
    public required DateTimeOffset LastSeen { get; init; }

    /// <summary>Distinct destinations seen, capped by the reader to keep memory bounded.</summary>
    public required IReadOnlyList<string> SampleDestinations { get; init; }
}
