using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace EVBlocker.Core.Monitor;

/// <summary>What <see cref="TcpConnectionCloser.CloseAll"/> did.</summary>
/// <param name="Closed">IPv4 connections reset.</param>
/// <param name="Failed">IPv4 connections Windows refused to reset, usually for lack of rights.</param>
/// <param name="LeftToFirewall">
/// IPv6 connections. Windows has no call to reset one, so these are left to the firewall, which
/// re-authorizes existing connections when its filters change.
/// </param>
public sealed record CloseResult(int Closed, int Failed, int LeftToFirewall);

/// <summary>
/// Cuts the open TCP connections of one executable at once.
/// </summary>
/// <remarks>
/// Taking an executable off the allow-list only decides what the firewall does next. A connection
/// already carrying traffic - a download, a sync - is not something the person pressing "Hủy"
/// expects to see finish. Resetting it makes the program reconnect, and that new attempt is the
/// one the firewall refuses.
///
/// UDP has no connections to reset; its next packet meets the firewall like any other.
/// </remarks>
public sealed class TcpConnectionCloser
{
    private readonly IConnectionScanner _scanner;

    public TcpConnectionCloser(IConnectionScanner scanner)
    {
        ArgumentNullException.ThrowIfNull(scanner);
        _scanner = scanner;
    }

    public CloseResult CloseAll(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        int closed = 0, failed = 0, leftToFirewall = 0;

        foreach (ConnectionRecord record in SelectTargets(_scanner.Scan(), executablePath))
        {
            if (record.Local.AddressFamily != AddressFamily.InterNetwork)
            {
                leftToFirewall++;
                continue;
            }

            IpHlpApi.MibTcpRow row = ToRow(record);
            if (IpHlpApi.SetTcpEntry(ref row) == IpHlpApi.NO_ERROR)
            {
                closed++;
            }
            else
            {
                failed++;
            }
        }

        return new CloseResult(closed, failed, leftToFirewall);
    }

    /// <summary>
    /// The TCP connections owned by <paramref name="executablePath"/> that have a far end.
    /// </summary>
    /// <remarks>
    /// Listening sockets are left alone: they carry no traffic out, and resetting one is not
    /// possible anyway. Connections already closing are skipped because there is nothing to cut.
    /// </remarks>
    internal static IReadOnlyList<ConnectionRecord> SelectTargets(
        IEnumerable<ConnectionRecord> records, string executablePath) =>
        records
            .Where(r => r.Protocol == TransportProtocol.Tcp
                        && r.Remote is not null
                        && (r.State is TcpConnectionState.Established
                            or TcpConnectionState.SynSent
                            or TcpConnectionState.SynReceived)
                        && string.Equals(r.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase))
            .ToList();

    internal static IpHlpApi.MibTcpRow ToRow(ConnectionRecord record) => new()
    {
        State = IpHlpApi.MIB_TCP_STATE_DELETE_TCB,
        LocalAddr = ToDword(record.Local.Address),
        LocalPort = IpHlpApi.EncodePort(record.Local.Port),
        RemoteAddr = ToDword(record.Remote!.Address),
        RemotePort = IpHlpApi.EncodePort(record.Remote.Port),
    };

    /// <summary>The address as the table stores it: its four bytes in memory order.</summary>
    private static uint ToDword(IPAddress address) =>
        MemoryMarshal.Read<uint>(address.GetAddressBytes());
}
