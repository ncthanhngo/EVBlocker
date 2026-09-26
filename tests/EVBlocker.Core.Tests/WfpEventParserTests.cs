using EVBlocker.Core.History;
using EVBlocker.Core.Monitor;

namespace EVBlocker.Core.Tests;

public sealed class WfpEventParserTests
{
    /// <summary>
    /// Stands in for the real QueryDosDevice lookup, which needs live volumes. Mirrors the one
    /// translation that matters: a kernel device path becomes a drive-letter path.
    /// </summary>
    private sealed class FakeDevicePathMapper : IDevicePathMapper
    {
        public string Normalize(string path) =>
            path.StartsWith(@"\device\harddiskvolume3\", StringComparison.OrdinalIgnoreCase)
                ? @"C:\" + path[@"\device\harddiskvolume3\".Length..]
                : path;
    }

    private static readonly IDevicePathMapper Mapper = new FakeDevicePathMapper();

    private static string BuildEventXml(
        int eventId = 5157,
        string application = @"\device\harddiskvolume3\program files\app\app.exe",
        string direction = "%%14593",
        string destAddress = "93.184.216.34",
        string destPort = "443",
        string protocol = "6",
        string processId = "4242",
        string systemTime = "2026-09-25T10:20:30.1234567Z")
    {
        return $"""
            <Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event">
              <System>
                <EventID>{eventId}</EventID>
                <TimeCreated SystemTime="{systemTime}" />
              </System>
              <EventData>
                <Data Name="ProcessID">{processId}</Data>
                <Data Name="Application">{application}</Data>
                <Data Name="Direction">{direction}</Data>
                <Data Name="SourceAddress">192.168.1.20</Data>
                <Data Name="SourcePort">51000</Data>
                <Data Name="DestAddress">{destAddress}</Data>
                <Data Name="DestPort">{destPort}</Data>
                <Data Name="Protocol">{protocol}</Data>
              </EventData>
            </Event>
            """;
    }

    [Fact]
    public void TryParse_BlockedOutboundEvent_MapsEveryField()
    {
        NetworkAttempt? attempt = WfpEventParser.TryParse(BuildEventXml(), Mapper);

        Assert.NotNull(attempt);
        Assert.True(attempt.WasBlocked);
        Assert.Equal(4242, attempt.ProcessId);
        Assert.Equal(@"C:\program files\app\app.exe", attempt.ExecutablePath);
        Assert.Equal(@"\device\harddiskvolume3\program files\app\app.exe", attempt.RawApplication);
        Assert.Equal("93.184.216.34", attempt.DestinationAddress?.ToString());
        Assert.Equal(443, attempt.DestinationPort);
        Assert.Equal(TransportProtocol.Tcp, attempt.Protocol);
        Assert.Equal(
            DateTimeOffset.Parse("2026-09-25T10:20:30.1234567Z", System.Globalization.CultureInfo.InvariantCulture),
            attempt.Timestamp);
    }

    [Fact]
    public void TryParse_AllowedEvent_IsNotMarkedBlocked()
    {
        NetworkAttempt? attempt = WfpEventParser.TryParse(BuildEventXml(eventId: 5156), Mapper);

        Assert.NotNull(attempt);
        Assert.False(attempt.WasBlocked);
    }

    [Fact]
    public void TryParse_InboundEvent_IsIgnored()
    {
        // %%14592 is Inbound. This product controls outbound only.
        Assert.Null(WfpEventParser.TryParse(BuildEventXml(direction: "%%14592"), Mapper));
    }

    [Fact]
    public void TryParse_RenderedOutboundDirection_IsAccepted()
    {
        // A caller may hand over already-rendered XML, where the reference becomes a word.
        Assert.NotNull(WfpEventParser.TryParse(BuildEventXml(direction: "Outbound"), Mapper));
    }

    [Theory]
    [InlineData(4624)]  // logon, the most common Security event - must not be misread as an attempt
    [InlineData(5158)]  // WFP bind permitted, a different subcategory
    public void TryParse_UnrelatedEventId_IsIgnored(int eventId)
    {
        Assert.Null(WfpEventParser.TryParse(BuildEventXml(eventId: eventId), Mapper));
    }

    [Fact]
    public void TryParse_MissingApplication_IsIgnored()
    {
        // System traffic with no owning image; the allow-list could not act on it.
        Assert.Null(WfpEventParser.TryParse(BuildEventXml(application: ""), Mapper));
    }

    [Fact]
    public void TryParse_UdpProtocol_IsRecognised()
    {
        NetworkAttempt? attempt = WfpEventParser.TryParse(BuildEventXml(protocol: "17"), Mapper);

        Assert.Equal(TransportProtocol.Udp, attempt?.Protocol);
    }

    [Fact]
    public void TryParse_UnmodelledProtocol_YieldsNullProtocolButKeepsTheRow()
    {
        // ICMP is real traffic; dropping the row would hide it from history entirely.
        NetworkAttempt? attempt = WfpEventParser.TryParse(BuildEventXml(protocol: "1"), Mapper);

        Assert.NotNull(attempt);
        Assert.Null(attempt.Protocol);
    }

    [Fact]
    public void TryParse_PathOutsideKnownDevice_IsLeftUnchanged()
    {
        const string unc = @"\device\mup\server\share\tool.exe";

        NetworkAttempt? attempt = WfpEventParser.TryParse(BuildEventXml(application: unc), Mapper);

        Assert.Equal(unc, attempt?.ExecutablePath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not xml at all")]
    [InlineData("<Event><unclosed>")]
    public void TryParse_MalformedInput_ReturnsNullInsteadOfThrowing(string xml)
    {
        // The Security log is shared with the rest of Windows, so odd input is normal, not fatal.
        Assert.Null(WfpEventParser.TryParse(xml, Mapper));
    }

    [Fact]
    public void TryParse_NullMapper_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => WfpEventParser.TryParse(BuildEventXml(), null!));
    }
}
