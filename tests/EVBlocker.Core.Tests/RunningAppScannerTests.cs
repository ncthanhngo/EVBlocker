using System.Net;
using EVBlocker.Core.Monitor;

namespace EVBlocker.Core.Tests;

/// <summary>Returns a fixed snapshot, so the scanner's grouping can be asserted.</summary>
internal sealed class FakeConnectionScanner : IConnectionScanner
{
    private readonly List<ConnectionRecord> _records = new();

    public void Add(string? executablePath, bool internet) => _records.Add(new ConnectionRecord
    {
        ProcessId = 1,
        ExecutablePath = executablePath,
        Protocol = TransportProtocol.Tcp,
        Local = new IPEndPoint(IPAddress.Loopback, 1000),
        Remote = new IPEndPoint(internet ? IPAddress.Parse("8.8.8.8") : IPAddress.Loopback, 443),
        State = TcpConnectionState.Established,
        IsRemoteInternet = internet,
    });

    public IReadOnlyList<ConnectionRecord> Scan() => _records;
}

public sealed class RunningAppScannerTests
{
    private static RunningAppScan Scan(FakeConnectionScanner? connections = null) =>
        new RunningAppScanner(connections ?? new FakeConnectionScanner()).Scan();

    [Fact]
    public void FindsSomething()
    {
        // The test host itself is running, so an empty result means enumeration failed.
        Assert.NotEmpty(Scan().Apps);
    }

    [Fact]
    public void ReportsHowMuchOfTheMachineItCouldRead()
    {
        // Without elevation most processes cannot be opened. A list that looked complete would be
        // a bad basis for deciding what to block, so the shortfall is part of the result.
        RunningAppScan scan = Scan();

        Assert.True(scan.ProcessesSeen > 0);
        Assert.InRange(scan.ProcessesUnreadable, 0, scan.ProcessesSeen);
    }

    [Fact]
    public void OneEntryPerExecutable_NotPerProcess()
    {
        RunningAppScan scan = Scan();

        Assert.Equal(
            scan.Apps.Count,
            scan.Apps.Select(a => a.ExecutablePath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void ProcessCountsAreAtLeastOne()
    {
        Assert.All(Scan().Apps, app => Assert.True(app.ProcessCount >= 1));
    }

    [Fact]
    public void EveryPathReported_ActuallyExists()
    {
        // These become allow-list entries, and an entry whose file is not there becomes a rule
        // that allows nothing.
        Assert.All(Scan().Apps, app => Assert.True(File.Exists(app.ExecutablePath), app.ExecutablePath));
    }

    [Fact]
    public void MarksTheExecutableThatHoldsAnInternetConnection()
    {
        string self = Environment.ProcessPath!;
        var connections = new FakeConnectionScanner();
        connections.Add(self, internet: true);

        RunningApp? entry = Scan(connections).Apps
            .FirstOrDefault(a => string.Equals(a.ExecutablePath, self, StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(entry);
        Assert.True(entry.HasInternetConnection);
    }

    [Fact]
    public void LoopbackOnlyConnection_DoesNotCountAsInternet()
    {
        string self = Environment.ProcessPath!;
        var connections = new FakeConnectionScanner();
        connections.Add(self, internet: false);

        RunningApp? entry = Scan(connections).Apps
            .FirstOrDefault(a => string.Equals(a.ExecutablePath, self, StringComparison.OrdinalIgnoreCase));

        Assert.False(entry?.HasInternetConnection);
    }

    [Fact]
    public void ConnectionWithNoResolvablePath_IsIgnoredRatherThanCrashing()
    {
        var connections = new FakeConnectionScanner();
        connections.Add(null, internet: true);

        Assert.NotEmpty(Scan(connections).Apps);
    }

    [Fact]
    public void FlagsExecutablesUnderTheWindowsDirectory()
    {
        // Allow-listing those by path is almost always wrong: the shared hosts live there, and
        // allowing svchost.exe by path allows every service inside it.
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        Assert.All(
            Scan().Apps,
            app => Assert.Equal(
                app.ExecutablePath.StartsWith(windows, StringComparison.OrdinalIgnoreCase),
                app.IsWindowsComponent));
    }

    [Fact]
    public void ConnectedApplicationsComeFirst()
    {
        string self = Environment.ProcessPath!;
        var connections = new FakeConnectionScanner();
        connections.Add(self, internet: true);

        IReadOnlyList<RunningApp> apps = Scan(connections).Apps;

        // The decisions that matter are the ones already reaching the internet.
        Assert.True(apps[0].HasInternetConnection);
    }
}
