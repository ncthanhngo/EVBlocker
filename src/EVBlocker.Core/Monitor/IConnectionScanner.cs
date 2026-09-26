namespace EVBlocker.Core.Monitor;

/// <summary>
/// Reads the sockets currently open on this machine, attributed to owning processes.
///
/// An interface so callers (and the UI) can be tested against a fake; the real implementation
/// needs live Windows tables and so cannot run in a unit test.
/// </summary>
public interface IConnectionScanner
{
    /// <summary>
    /// One snapshot of every TCP connection and UDP endpoint, across IPv4 and IPv6.
    /// Never throws for a partially readable table: rows that cannot be read are skipped.
    /// </summary>
    IReadOnlyList<ConnectionRecord> Scan();
}
