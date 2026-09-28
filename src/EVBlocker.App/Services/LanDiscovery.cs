using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using EVBlocker.Core.Ssh;

namespace EVBlocker.App.Services;

/// <summary>A machine that answered a discovery probe: the name it gave and where it answered from.</summary>
public sealed record DiscoveredMachine(string HostName, string Address);

/// <summary>
/// Finds the other EVBlocker machines on the local network, and answers when they look for this
/// one. No registration and no server: the admin's machine asks the subnet and everyone running
/// the app replies. Being on the same LAN is all it takes.
/// </summary>
/// <remarks>
/// The responder runs for as long as the app does, in the tray too, so a machine is findable
/// whenever EVBlocker is running on it. The prober is a separate short-lived socket, used when the
/// panel refreshes its list. Every socket path swallows its own errors: discovery is a convenience
/// on top of the manual list, so a blocked port or a down interface must degrade to "found none",
/// never crash the app.
/// </remarks>
public sealed class LanDiscovery : IDisposable
{
    private UdpClient? _responder;
    private CancellationTokenSource? _responderStop;

    /// <summary>Begins answering probes. Safe to call once; a second call is ignored.</summary>
    public void StartResponder()
    {
        if (_responder is not null)
        {
            return;
        }

        try
        {
            var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, LanDiscoveryProtocol.Port));

            _responder = udp;
            _responderStop = new CancellationTokenSource();
            _ = RespondLoopAsync(udp, _responderStop.Token);
        }
        catch (SocketException)
        {
            // The port is taken, or the machine has no usable stack. Discovery is simply off;
            // the manual list still works.
            _responder = null;
        }
    }

    private static async Task RespondLoopAsync(UdpClient udp, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await udp.ReceiveAsync(stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (!LanDiscoveryProtocol.IsProbe(received.Buffer))
            {
                continue;
            }

            byte[] reply = LanDiscoveryProtocol.Reply(Environment.MachineName);
            try
            {
                await udp.SendAsync(reply, received.RemoteEndPoint, stop).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException)
            {
                // The prober will retry on its own schedule; a dropped reply is not worth more.
            }
        }
    }

    /// <summary>
    /// Broadcasts a probe and gathers replies for <paramref name="window"/>.
    /// </summary>
    /// <remarks>
    /// Keyed by address so a machine answering on several interfaces is listed once. This machine's
    /// own reply is dropped: the admin is looking for the others, not itself.
    /// </remarks>
    public static async Task<IReadOnlyList<DiscoveredMachine>> DiscoverAsync(TimeSpan window)
    {
        var found = new Dictionary<string, DiscoveredMachine>(StringComparer.OrdinalIgnoreCase);
        string self = Environment.MachineName;

        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

            byte[] probe = LanDiscoveryProtocol.Probe();
            var target = new IPEndPoint(IPAddress.Broadcast, LanDiscoveryProtocol.Port);
            await udp.SendAsync(probe, target).ConfigureAwait(false);

            using var cts = new CancellationTokenSource(window);
            while (true)
            {
                UdpReceiveResult received;
                try
                {
                    received = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException)
                {
                    continue;
                }

                if (LanDiscoveryProtocol.TryReadReply(received.Buffer, out string hostName)
                    && !string.Equals(hostName, self, StringComparison.OrdinalIgnoreCase))
                {
                    string address = received.RemoteEndPoint.Address.ToString();
                    found[address] = new DiscoveredMachine(hostName, address);
                }
            }
        }
        catch (SocketException)
        {
            // No usable interface; return whatever was gathered before it failed.
        }

        return found.Values.ToList();
    }

    public void Dispose()
    {
        try
        {
            _responderStop?.Cancel();
            _responder?.Dispose();
            _responderStop?.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
