using EVBlocker.Core.Monitor;

namespace EVBlocker.Core.Tests;

/// <summary>
/// Reads the real Windows tables, so these assert shape rather than specific rows.
///
/// Their job is to catch a silent P/Invoke regression: a wrong struct offset or a broken port
/// decode still compiles and still returns rows, just garbage ones. Assertions are kept loose
/// enough not to depend on which sockets happen to be open.
/// </summary>
public sealed class ActiveConnectionScannerSmokeTests
{
    private static readonly IReadOnlyList<ConnectionRecord> Snapshot = new ActiveConnectionScanner().Scan();

    [Fact]
    public void Scan_ReturnsRows()
    {
        // Windows always has listening sockets (RPC, SMB), so an empty result means the table
        // read failed rather than that the machine is quiet.
        Assert.NotEmpty(Snapshot);
    }

    [Fact]
    public void Scan_EveryPortIsInRange()
    {
        // A wrong offset or a missing byte swap shows up here first: misread bytes land outside
        // the valid port range far more often than inside it.
        Assert.All(Snapshot, record =>
        {
            Assert.InRange(record.Local.Port, 0, 65535);

            if (record.Remote is not null)
            {
                Assert.InRange(record.Remote.Port, 0, 65535);
            }
        });
    }

    [Fact]
    public void Scan_EveryStateIsDefined()
    {
        // Catches a shifted State field: garbage rarely lands on a valid enum member.
        Assert.All(
            Snapshot.Where(r => r.Protocol == TransportProtocol.Tcp),
            record => Assert.True(
                record.State.HasValue && Enum.IsDefined(record.State.Value),
                $"Undefined TCP state: {record.State}"));
    }

    [Fact]
    public void Scan_ProcessIdsAreNonNegative()
    {
        Assert.All(Snapshot, record => Assert.True(record.ProcessId >= 0));
    }

    [Fact]
    public void Scan_ListeningSocketsHaveNoRemote()
    {
        Assert.All(
            Snapshot.Where(r => r.State == TcpConnectionState.Listen),
            record => Assert.Null(record.Remote));
    }

    [Fact]
    public void Scan_UdpRowsCarryNoRemote()
    {
        // Documented API limitation, asserted so a future change cannot quietly imply otherwise.
        Assert.All(
            Snapshot.Where(r => r.Protocol == TransportProtocol.Udp),
            record =>
            {
                Assert.Null(record.Remote);
                Assert.False(record.IsRemoteInternet);
                Assert.Null(record.State);
            });
    }

    [Fact]
    public void Scan_InternetFlagAgreesWithClassifier()
    {
        Assert.All(Snapshot, record => Assert.Equal(
            record.Remote is not null && AddressClassifier.IsInternet(record.Remote.Address),
            record.IsRemoteInternet));
    }

    [Fact]
    public void Scan_ResolvesAtLeastOneExecutablePath()
    {
        // Without elevation most processes cannot be opened, so this only asserts that the
        // resolver works at all - the current process is always readable by itself.
        Assert.Contains(Snapshot, record => record.ExecutablePath is not null);
    }
}
