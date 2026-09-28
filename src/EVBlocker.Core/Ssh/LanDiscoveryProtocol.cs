using System.Text;

namespace EVBlocker.Core.Ssh;

/// <summary>The datagrams EVBlocker machines exchange to find each other on the local network.</summary>
/// <remarks>
/// A machine's address is taken from the UDP sender, never from the payload, so a reply cannot
/// claim to be at an address it is not sending from. The magic prefix is a filter against stray
/// traffic on the port, not a secret: discovery only reveals a name and an address, both of which
/// anyone on the subnet can already see.
/// </remarks>
public static class LanDiscoveryProtocol
{
    /// <summary>The UDP port both the question and the answers use.</summary>
    public const int Port = 50505;

    private const string Magic = "EVBLOCKER-DISCOVERY-1";
    private const string ProbeVerb = "PROBE";
    private const string ReplyVerb = "HELLO";

    private static readonly Encoding Text = Encoding.UTF8;

    /// <summary>The broadcast question: "who here runs EVBlocker?".</summary>
    public static byte[] Probe() => Text.GetBytes($"{Magic}\n{ProbeVerb}");

    public static bool IsProbe(ReadOnlySpan<byte> datagram) =>
        Decode(datagram) is [Magic, ProbeVerb, ..];

    /// <summary>One machine's answer, carrying the name to show for it.</summary>
    public static byte[] Reply(string hostName)
    {
        ArgumentNullException.ThrowIfNull(hostName);

        // Newlines would break the line framing; a host name cannot contain them, but stripping
        // keeps a malformed one from corrupting the message rather than trusting it not to.
        string clean = hostName.Replace("\n", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal);
        return Text.GetBytes($"{Magic}\n{ReplyVerb}\n{clean}");
    }

    /// <summary>Reads a reply's host name, or fails if the datagram is not one.</summary>
    public static bool TryReadReply(ReadOnlySpan<byte> datagram, out string hostName)
    {
        hostName = string.Empty;

        if (Decode(datagram) is [Magic, ReplyVerb, string name, ..])
        {
            hostName = name;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Splits a datagram into its lines, or null if it is not text or not one of ours. Bounded so
    /// a random large datagram on the port cannot turn into a large allocation.
    /// </summary>
    private static string[]? Decode(ReadOnlySpan<byte> datagram)
    {
        if (datagram.Length is 0 or > 512)
        {
            return null;
        }

        try
        {
            return Text.GetString(datagram).Split('\n');
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}
