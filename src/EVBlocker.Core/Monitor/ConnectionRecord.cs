using System.Net;

namespace EVBlocker.Core.Monitor;

public enum TransportProtocol
{
    Tcp,
    Udp,
}

/// <summary>
/// One socket owned by one process, as reported by the Windows TCP/UDP tables.
/// </summary>
/// <remarks>
/// UDP has an important gap: Windows exposes only the local endpoint in the UDP table, with no
/// remote address, even for a connected UDP socket. So a UDP row always has
/// <see cref="Remote"/> = null and <see cref="IsRemoteInternet"/> = false, and UDP-based traffic
/// (QUIC / HTTP3, DNS) cannot be attributed to a destination by scanning alone.
///
/// The WFP audit log covers that gap - events 5156/5157 do carry remote addresses for UDP - which
/// is why the history feature is not merely a nicety here but the only view of UDP destinations.
/// </remarks>
public sealed record ConnectionRecord
{
    public required int ProcessId { get; init; }

    /// <summary>
    /// Full path of the owning executable, or null when it could not be resolved because the
    /// process exited between the table read and the lookup, or is protected from being opened.
    /// </summary>
    public string? ExecutablePath { get; init; }

    public required TransportProtocol Protocol { get; init; }

    public required IPEndPoint Local { get; init; }

    /// <summary>Null for a listening TCP socket and for every UDP row (see the remarks above).</summary>
    public IPEndPoint? Remote { get; init; }

    /// <summary>TCP connection state, or null for UDP which is stateless.</summary>
    public TcpConnectionState? State { get; init; }

    /// <summary>
    /// True when <see cref="Remote"/> is a destination outside this machine and its LAN.
    /// Always false for UDP, since no remote address is available.
    /// </summary>
    public required bool IsRemoteInternet { get; init; }
}

/// <summary>TCP states as reported by MIB_TCPROW. Values match the Windows MIB_TCP_STATE enum.</summary>
public enum TcpConnectionState
{
    Closed = 1,
    Listen = 2,
    SynSent = 3,
    SynReceived = 4,
    Established = 5,
    FinWait1 = 6,
    FinWait2 = 7,
    CloseWait = 8,
    Closing = 9,
    LastAck = 10,
    TimeWait = 11,
    DeleteTcb = 12,
}
