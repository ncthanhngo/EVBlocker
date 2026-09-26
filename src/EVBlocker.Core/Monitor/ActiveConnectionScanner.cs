using System.Buffers;
using System.Buffers.Binary;
using System.Net;

namespace EVBlocker.Core.Monitor;

/// <summary>
/// Reads the live Windows TCP/UDP tables via iphlpapi and attributes each socket to its process.
/// </summary>
public sealed class ActiveConnectionScanner : IConnectionScanner
{
    /// <summary>
    /// The table can grow between the size probe and the fetch, so the fetch may need retrying.
    /// A small bound stops a pathological churn loop from spinning forever.
    /// </summary>
    private const int MaxTableFetchAttempts = 4;

    private enum TableKind
    {
        Tcp4,
        Tcp6,
        Udp4,
        Udp6,
    }

    public IReadOnlyList<ConnectionRecord> Scan()
    {
        var results = new List<ConnectionRecord>(capacity: 256);

        // Per-scan only, deliberately not a field: a process id resolved in this snapshot stays
        // valid for this snapshot, but caching across scans would risk attributing a connection
        // to the wrong executable after a PID is recycled. Many rows share a PID (a browser has
        // dozens), so this still collapses the lookups that matter.
        var pathsByPid = new Dictionary<int, string?>();

        foreach (TableKind kind in Enum.GetValues<TableKind>())
        {
            ReadTable(kind, results, pathsByPid);
        }

        return results;
    }

    private static unsafe void ReadTable(
        TableKind kind, List<ConnectionRecord> results, Dictionary<int, string?> pathsByPid)
    {
        bool isTcp = kind is TableKind.Tcp4 or TableKind.Tcp6;
        int family = kind is TableKind.Tcp4 or TableKind.Udp4 ? IpHlpApi.AF_INET : IpHlpApi.AF_INET6;
        int tableClass = isTcp ? IpHlpApi.TCP_TABLE_OWNER_PID_ALL : IpHlpApi.UDP_TABLE_OWNER_PID;

        byte[]? rented = null;
        int size = 0;

        try
        {
            for (int attempt = 0; attempt < MaxTableFetchAttempts; attempt++)
            {
                if (size == 0)
                {
                    uint probe = Fetch(IntPtr.Zero, ref size);

                    // "Buffer too small" is the expected answer to a size probe. Anything else
                    // other than success is a failure this code cannot recover from, and an
                    // empty table reports success with size 0.
                    if (probe != IpHlpApi.ERROR_INSUFFICIENT_BUFFER && probe != IpHlpApi.NO_ERROR)
                    {
                        return;
                    }

                    if (size == 0)
                    {
                        return;
                    }
                }

                if (rented is null || rented.Length < size)
                {
                    if (rented is not null)
                    {
                        ArrayPool<byte>.Shared.Return(rented);
                    }

                    rented = ArrayPool<byte>.Shared.Rent(size);
                }

                uint result;
                fixed (byte* buffer = rented)
                {
                    result = Fetch((IntPtr)buffer, ref size);
                }

                if (result == IpHlpApi.NO_ERROR)
                {
                    Parse(kind, rented.AsSpan(0, size), results, pathsByPid);
                    return;
                }

                // The table grew between the probe and the fetch. size now holds the larger
                // requirement, so loop round and retry with a bigger buffer.
                if (result != IpHlpApi.ERROR_INSUFFICIENT_BUFFER)
                {
                    return;
                }
            }
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        uint Fetch(IntPtr buffer, ref int bufferSize) => isTcp
            ? IpHlpApi.GetExtendedTcpTable(buffer, ref bufferSize, 0, family, tableClass, 0)
            : IpHlpApi.GetExtendedUdpTable(buffer, ref bufferSize, 0, family, tableClass, 0);
    }

    private static void Parse(
        TableKind kind,
        ReadOnlySpan<byte> table,
        List<ConnectionRecord> results,
        Dictionary<int, string?> pathsByPid)
    {
        if (table.Length < IpHlpApi.TableRowsOffset)
        {
            return;
        }

        uint declaredCount = BinaryPrimitives.ReadUInt32LittleEndian(table);
        ReadOnlySpan<byte> rows = table[IpHlpApi.TableRowsOffset..];

        int rowSize = kind switch
        {
            TableKind.Tcp4 => IpHlpApi.Tcp4Row.Size,
            TableKind.Tcp6 => IpHlpApi.Tcp6Row.Size,
            TableKind.Udp4 => IpHlpApi.Udp4Row.Size,
            _ => IpHlpApi.Udp6Row.Size,
        };

        // Trust the buffer over the declared count: never read past what was actually returned.
        int count = (int)Math.Min(declaredCount, (uint)(rows.Length / rowSize));

        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> row = rows.Slice(i * rowSize, rowSize);

            results.Add(kind switch
            {
                TableKind.Tcp4 => ParseTcp4(row, pathsByPid),
                TableKind.Tcp6 => ParseTcp6(row, pathsByPid),
                TableKind.Udp4 => ParseUdp4(row, pathsByPid),
                _ => ParseUdp6(row, pathsByPid),
            });
        }
    }

    private static ConnectionRecord ParseTcp4(ReadOnlySpan<byte> row, Dictionary<int, string?> pathsByPid)
    {
        var state = (TcpConnectionState)ReadDword(row, IpHlpApi.Tcp4Row.State);
        int pid = (int)ReadDword(row, IpHlpApi.Tcp4Row.OwningPid);

        var local = new IPEndPoint(
            ReadAddressV4(row, IpHlpApi.Tcp4Row.LocalAddr),
            IpHlpApi.DecodePort(ReadDword(row, IpHlpApi.Tcp4Row.LocalPort)));

        // A listening socket has no peer; Windows fills the remote fields with 0.0.0.0:0.
        IPEndPoint? remote = state == TcpConnectionState.Listen
            ? null
            : new IPEndPoint(
                ReadAddressV4(row, IpHlpApi.Tcp4Row.RemoteAddr),
                IpHlpApi.DecodePort(ReadDword(row, IpHlpApi.Tcp4Row.RemotePort)));

        return new ConnectionRecord
        {
            ProcessId = pid,
            ExecutablePath = ResolvePath(pid, pathsByPid),
            Protocol = TransportProtocol.Tcp,
            Local = local,
            Remote = remote,
            State = state,
            IsRemoteInternet = remote is not null && AddressClassifier.IsInternet(remote.Address),
        };
    }

    private static ConnectionRecord ParseTcp6(ReadOnlySpan<byte> row, Dictionary<int, string?> pathsByPid)
    {
        var state = (TcpConnectionState)ReadDword(row, IpHlpApi.Tcp6Row.State);
        int pid = (int)ReadDword(row, IpHlpApi.Tcp6Row.OwningPid);

        var local = new IPEndPoint(
            ReadAddressV6(row, IpHlpApi.Tcp6Row.LocalAddr, ReadDword(row, IpHlpApi.Tcp6Row.LocalScopeId)),
            IpHlpApi.DecodePort(ReadDword(row, IpHlpApi.Tcp6Row.LocalPort)));

        IPEndPoint? remote = state == TcpConnectionState.Listen
            ? null
            : new IPEndPoint(
                ReadAddressV6(row, IpHlpApi.Tcp6Row.RemoteAddr, ReadDword(row, IpHlpApi.Tcp6Row.RemoteScopeId)),
                IpHlpApi.DecodePort(ReadDword(row, IpHlpApi.Tcp6Row.RemotePort)));

        return new ConnectionRecord
        {
            ProcessId = pid,
            ExecutablePath = ResolvePath(pid, pathsByPid),
            Protocol = TransportProtocol.Tcp,
            Local = local,
            Remote = remote,
            State = state,
            IsRemoteInternet = remote is not null && AddressClassifier.IsInternet(remote.Address),
        };
    }

    // Both UDP rows carry no remote address at all - see the remarks on ConnectionRecord.
    private static ConnectionRecord ParseUdp4(ReadOnlySpan<byte> row, Dictionary<int, string?> pathsByPid)
    {
        int pid = (int)ReadDword(row, IpHlpApi.Udp4Row.OwningPid);

        return new ConnectionRecord
        {
            ProcessId = pid,
            ExecutablePath = ResolvePath(pid, pathsByPid),
            Protocol = TransportProtocol.Udp,
            Local = new IPEndPoint(
                ReadAddressV4(row, IpHlpApi.Udp4Row.LocalAddr),
                IpHlpApi.DecodePort(ReadDword(row, IpHlpApi.Udp4Row.LocalPort))),
            Remote = null,
            State = null,
            IsRemoteInternet = false,
        };
    }

    private static ConnectionRecord ParseUdp6(ReadOnlySpan<byte> row, Dictionary<int, string?> pathsByPid)
    {
        int pid = (int)ReadDword(row, IpHlpApi.Udp6Row.OwningPid);

        return new ConnectionRecord
        {
            ProcessId = pid,
            ExecutablePath = ResolvePath(pid, pathsByPid),
            Protocol = TransportProtocol.Udp,
            Local = new IPEndPoint(
                ReadAddressV6(row, IpHlpApi.Udp6Row.LocalAddr, ReadDword(row, IpHlpApi.Udp6Row.LocalScopeId)),
                IpHlpApi.DecodePort(ReadDword(row, IpHlpApi.Udp6Row.LocalPort))),
            Remote = null,
            State = null,
            IsRemoteInternet = false,
        };
    }

    private static uint ReadDword(ReadOnlySpan<byte> row, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(row[offset..]);

    /// <summary>
    /// The 4 address bytes are already stored in network order, which is what IPAddress expects.
    /// </summary>
    private static IPAddress ReadAddressV4(ReadOnlySpan<byte> row, int offset) =>
        new(row.Slice(offset, 4));

    /// <summary>
    /// The scope id matters for link-local addresses; without it two fe80:: peers on different
    /// interfaces would be indistinguishable.
    /// </summary>
    private static IPAddress ReadAddressV6(ReadOnlySpan<byte> row, int offset, uint scopeId) =>
        new(row.Slice(offset, IpHlpApi.AddressV6Length), scopeId);

    private static string? ResolvePath(int pid, Dictionary<int, string?> pathsByPid)
    {
        if (pathsByPid.TryGetValue(pid, out string? cached))
        {
            return cached;
        }

        string? path = ProcessPathResolver.TryGetPath(pid);
        pathsByPid[pid] = path;
        return path;
    }
}
