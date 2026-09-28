using System.Text;
using EVBlocker.Core.Ssh;

namespace EVBlocker.Core.Tests;

public sealed class LanDiscoveryProtocolTests
{
    [Fact]
    public void Probe_IsRecognisedAsAProbe_NotAsAReply()
    {
        byte[] probe = LanDiscoveryProtocol.Probe();

        Assert.True(LanDiscoveryProtocol.IsProbe(probe));
        Assert.False(LanDiscoveryProtocol.TryReadReply(probe, out _));
    }

    [Fact]
    public void Reply_CarriesTheHostName_AndIsNotAProbe()
    {
        byte[] reply = LanDiscoveryProtocol.Reply("PC-KYTHUAT-01");

        Assert.True(LanDiscoveryProtocol.TryReadReply(reply, out string name));
        Assert.Equal("PC-KYTHUAT-01", name);
        Assert.False(LanDiscoveryProtocol.IsProbe(reply));
    }

    [Fact]
    public void Reply_StripsNewlines_SoFramingCannotBeBroken()
    {
        byte[] reply = LanDiscoveryProtocol.Reply("evil\nHELLO\nspoof");

        Assert.True(LanDiscoveryProtocol.TryReadReply(reply, out string name));
        Assert.Equal("evilHELLOspoof", name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("random noise on the port")]
    [InlineData("EVBLOCKER-DISCOVERY-1")]
    public void ForeignOrEmptyDatagrams_AreNeitherProbeNorReply(string payload)
    {
        byte[] datagram = Encoding.UTF8.GetBytes(payload);

        Assert.False(LanDiscoveryProtocol.IsProbe(datagram));
        Assert.False(LanDiscoveryProtocol.TryReadReply(datagram, out _));
    }

    [Fact]
    public void OversizedDatagram_IsRejected()
    {
        byte[] tooBig = new byte[513];

        Assert.False(LanDiscoveryProtocol.IsProbe(tooBig));
        Assert.False(LanDiscoveryProtocol.TryReadReply(tooBig, out _));
    }
}
