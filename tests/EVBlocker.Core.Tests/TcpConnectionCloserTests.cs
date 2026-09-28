using System.Net;
using EVBlocker.Core.Monitor;

namespace EVBlocker.Core.Tests;

public sealed class TcpConnectionCloserTests
{
    private const string Chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";

    private static ConnectionRecord Tcp(
        string? path,
        TcpConnectionState state = TcpConnectionState.Established,
        string remote = "142.250.66.78:443") => new()
    {
        ProcessId = 1234,
        ExecutablePath = path,
        Protocol = TransportProtocol.Tcp,
        Local = IPEndPoint.Parse("192.168.1.10:51000"),
        Remote = IPEndPoint.Parse(remote),
        State = state,
        IsRemoteInternet = true,
    };

    [Fact]
    public void SelectTargets_TakesOnlyLiveConnectionsOfThatExecutable()
    {
        ConnectionRecord live = Tcp(Chrome);
        ConnectionRecord opening = Tcp(Chrome, TcpConnectionState.SynSent);
        ConnectionRecord otherCase = Tcp(Chrome.ToUpperInvariant());

        IReadOnlyList<ConnectionRecord> targets = TcpConnectionCloser.SelectTargets(
            new[]
            {
                live,
                opening,
                otherCase,
                Tcp(@"C:\Other\app.exe"),
                Tcp(null),
                Tcp(Chrome, TcpConnectionState.Listen),
                Tcp(Chrome, TcpConnectionState.TimeWait),
                Tcp(Chrome) with { Protocol = TransportProtocol.Udp, State = null },
                Tcp(Chrome) with { Remote = null },
            },
            Chrome);

        Assert.Equal(new[] { live, opening, otherCase }, targets);
    }

    [Theory]
    [InlineData(443)]
    [InlineData(80)]
    [InlineData(51000)]
    [InlineData(1)]
    [InlineData(65535)]
    public void EncodePort_RoundTripsThroughDecodePort(int port)
    {
        Assert.Equal(port, IpHlpApi.DecodePort(IpHlpApi.EncodePort(port)));
    }

    [Fact]
    public void ToRow_WritesAddressesAndPortsAsTheTableStoresThem()
    {
        IpHlpApi.MibTcpRow row = TcpConnectionCloser.ToRow(Tcp(Chrome));

        Assert.Equal(IpHlpApi.MIB_TCP_STATE_DELETE_TCB, row.State);

        // Network byte order in memory: 192.168.1.10 is the bytes C0 A8 01 0A.
        Assert.Equal(new byte[] { 192, 168, 1, 10 }, BitConverter.GetBytes(row.LocalAddr));
        Assert.Equal(new byte[] { 142, 250, 66, 78 }, BitConverter.GetBytes(row.RemoteAddr));

        // Port 443 is 0x01BB; stored big-endian in the low two bytes.
        Assert.Equal(new byte[] { 0x01, 0xBB, 0, 0 }, BitConverter.GetBytes(row.RemotePort));
    }
}
