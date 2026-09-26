using System.Globalization;
using System.Net;
using System.Xml.Linq;
using EVBlocker.Core.Monitor;

namespace EVBlocker.Core.History;

/// <summary>
/// Turns the raw XML of a WFP audit event into a <see cref="NetworkAttempt"/>.
///
/// Split out from the log reader on purpose: this is the part with real branching (missing
/// fields, direction filtering, device-path translation, odd protocols) and it is pure, so it can
/// be unit tested against sample XML without an elevated process or a live Security log.
/// </summary>
public static class WfpEventParser
{
    /// <summary>WFP blocked a connection.</summary>
    public const int BlockedEventId = 5157;

    /// <summary>WFP permitted a connection.</summary>
    public const int AllowedEventId = 5156;

    /// <summary>
    /// Windows writes direction as a message-table reference in raw event XML. %%14593 is
    /// Outbound, %%14592 is Inbound. The rendered form is accepted too, since a caller may hand
    /// over already-rendered XML.
    /// </summary>
    private const string OutboundReference = "%%14593";

    private const int ProtocolTcp = 6;
    private const int ProtocolUdp = 17;

    private static readonly XNamespace EventNs = "http://schemas.microsoft.com/win/2004/08/events/event";

    /// <summary>
    /// Returns null - rather than throwing - for any event this feature does not care about:
    /// a different event id, an inbound attempt, or XML missing the fields that make a row
    /// meaningful. The Security log is shared with the rest of Windows, so unrelated and
    /// unexpected shapes are normal input, not errors.
    /// </summary>
    public static NetworkAttempt? TryParse(string eventXml, IDevicePathMapper mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);

        if (string.IsNullOrWhiteSpace(eventXml))
        {
            return null;
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(eventXml);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }

        XElement? root = document.Root;
        if (root is null)
        {
            return null;
        }

        XElement? system = root.Element(EventNs + "System");
        if (system is null)
        {
            return null;
        }

        if (!int.TryParse(
                system.Element(EventNs + "EventID")?.Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int eventId))
        {
            return null;
        }

        if (eventId != BlockedEventId && eventId != AllowedEventId)
        {
            return null;
        }

        Dictionary<string, string> data = ReadEventData(root);

        // Inbound attempts are out of scope: this product controls outbound access only.
        if (data.TryGetValue("Direction", out string? direction)
            && !IsOutbound(direction))
        {
            return null;
        }

        if (!data.TryGetValue("Application", out string? rawApplication)
            || string.IsNullOrWhiteSpace(rawApplication))
        {
            // System-level traffic with no owning image; nothing the allow-list could act on.
            return null;
        }

        return new NetworkAttempt
        {
            RawApplication = rawApplication,
            ExecutablePath = mapper.Normalize(rawApplication),
            ProcessId = ParseInt(data, "ProcessID") ?? 0,
            Timestamp = ReadTimestamp(system),
            WasBlocked = eventId == BlockedEventId,
            DestinationAddress = ParseAddress(data, "DestAddress"),
            DestinationPort = ParseInt(data, "DestPort"),
            Protocol = ParseProtocol(data),
        };
    }

    private static bool IsOutbound(string direction) =>
        direction.Contains(OutboundReference, StringComparison.Ordinal)
        || direction.Equals("Outbound", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> ReadEventData(XElement root)
    {
        var data = new Dictionary<string, string>(StringComparer.Ordinal);

        XElement? eventData = root.Element(EventNs + "EventData");
        if (eventData is null)
        {
            return data;
        }

        foreach (XElement element in eventData.Elements(EventNs + "Data"))
        {
            string? name = element.Attribute("Name")?.Value;
            if (!string.IsNullOrEmpty(name))
            {
                data[name] = element.Value;
            }
        }

        return data;
    }

    private static DateTimeOffset ReadTimestamp(XElement system)
    {
        string? raw = system.Element(EventNs + "TimeCreated")?.Attribute("SystemTime")?.Value;

        return DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out DateTimeOffset parsed)
            ? parsed
            : default;
    }

    private static int? ParseInt(Dictionary<string, string> data, string key) =>
        data.TryGetValue(key, out string? raw)
        && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : null;

    private static IPAddress? ParseAddress(Dictionary<string, string> data, string key) =>
        data.TryGetValue(key, out string? raw) && IPAddress.TryParse(raw, out IPAddress? address)
            ? address
            : null;

    private static TransportProtocol? ParseProtocol(Dictionary<string, string> data) =>
        ParseInt(data, "Protocol") switch
        {
            ProtocolTcp => TransportProtocol.Tcp,
            ProtocolUdp => TransportProtocol.Udp,
            // ICMP, GRE and friends are real but not modelled; reported as unknown, not dropped.
            _ => null,
        };
}
