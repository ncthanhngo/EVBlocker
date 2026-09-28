using System.Runtime.InteropServices;

namespace EVBlocker.Core.Monitor;

/// <summary>
/// P/Invoke surface for the Windows TCP/UDP connection tables in iphlpapi.dll.
///
/// Uses [LibraryImport] source-generated interop rather than [DllImport] so the published
/// single-file build survives trimming.
///
/// Rows are described as byte offsets rather than as structs. The IPv6 rows embed a 16-byte
/// address, which as a struct field needs either a fixed-size buffer (forcing the whole parser
/// into unsafe code) or a MarshalAs attribute (which makes the struct non-blittable and pulls in
/// marshalling on a path polled once a second). Offsets keep the parser safe and allocation-free,
/// with the native layout documented in exactly one place - here.
/// </summary>
internal static partial class IpHlpApi
{
    internal const int AF_INET = 2;
    internal const int AF_INET6 = 23;

    internal const uint NO_ERROR = 0;
    internal const uint ERROR_INSUFFICIENT_BUFFER = 122;

    /// <summary>TCP_TABLE_CLASS value for a table keyed by owning process id.</summary>
    internal const int TCP_TABLE_OWNER_PID_ALL = 5;

    /// <summary>UDP_TABLE_CLASS value for a table keyed by owning process id.</summary>
    internal const int UDP_TABLE_OWNER_PID = 1;

    /// <summary>
    /// Every table begins with DWORD dwNumEntries, then the rows. All four row types are
    /// 4-byte aligned, so the first row always starts at offset 4.
    /// </summary>
    internal const int TableRowsOffset = 4;

    /// <summary>MIB_TCPROW_OWNER_PID: State, LocalAddr, LocalPort, RemoteAddr, RemotePort, OwningPid.</summary>
    internal static class Tcp4Row
    {
        internal const int Size = 24;
        internal const int State = 0;
        internal const int LocalAddr = 4;
        internal const int LocalPort = 8;
        internal const int RemoteAddr = 12;
        internal const int RemotePort = 16;
        internal const int OwningPid = 20;
    }

    /// <summary>MIB_TCP6ROW_OWNER_PID: the addresses are 16-byte in_addr6 values.</summary>
    internal static class Tcp6Row
    {
        internal const int Size = 56;
        internal const int LocalAddr = 0;
        internal const int LocalScopeId = 16;
        internal const int LocalPort = 20;
        internal const int RemoteAddr = 24;
        internal const int RemoteScopeId = 40;
        internal const int RemotePort = 44;
        internal const int State = 48;
        internal const int OwningPid = 52;
    }

    /// <summary>MIB_UDPROW_OWNER_PID. UDP exposes no remote address, by design of the API.</summary>
    internal static class Udp4Row
    {
        internal const int Size = 12;
        internal const int LocalAddr = 0;
        internal const int LocalPort = 4;
        internal const int OwningPid = 8;
    }

    /// <summary>MIB_UDP6ROW_OWNER_PID. UDP exposes no remote address, by design of the API.</summary>
    internal static class Udp6Row
    {
        internal const int Size = 28;
        internal const int LocalAddr = 0;
        internal const int LocalScopeId = 16;
        internal const int LocalPort = 20;
        internal const int OwningPid = 24;
    }

    internal const int AddressV6Length = 16;

    // bOrder is a Win32 BOOL. Declared as int rather than bool to keep the signature blittable.
    [LibraryImport("iphlpapi.dll")]
    internal static partial uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref int pdwSize, int bOrder, int ulAf, int tableClass, uint reserved);

    [LibraryImport("iphlpapi.dll")]
    internal static partial uint GetExtendedUdpTable(
        IntPtr pUdpTable, ref int pdwSize, int bOrder, int ulAf, int tableClass, uint reserved);

    /// <summary>
    /// MIB_TCPROW, the input to SetTcpEntry. Five DWORDs, so a plain struct is blittable here -
    /// unlike the table rows above, it carries no IPv6 address.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MibTcpRow
    {
        internal uint State;
        internal uint LocalAddr;
        internal uint LocalPort;
        internal uint RemoteAddr;
        internal uint RemotePort;
    }

    /// <summary>The only state SetTcpEntry accepts: tear the connection down with a reset.</summary>
    internal const uint MIB_TCP_STATE_DELETE_TCB = 12;

    /// <summary>
    /// Resets one IPv4 TCP connection. Needs administrator rights. There is no IPv6 counterpart.
    /// </summary>
    [LibraryImport("iphlpapi.dll")]
    internal static partial uint SetTcpEntry(ref MibTcpRow row);

    /// <summary>
    /// Ports in these tables sit in the low two bytes of a DWORD, in network byte order.
    /// The upper two bytes are padding and must be ignored.
    /// </summary>
    internal static int DecodePort(uint raw)
    {
        return (int)((raw & 0x00FF) << 8 | (raw & 0xFF00) >> 8);
    }

    /// <summary>The inverse of <see cref="DecodePort"/>.</summary>
    internal static uint EncodePort(int port)
    {
        return (uint)((port & 0x00FF) << 8 | (port & 0xFF00) >> 8);
    }
}
