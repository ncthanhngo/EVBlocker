using EVBlocker.Core.Monitor;

namespace EVBlocker.App.ViewModels;

/// <summary>Display projection of one <see cref="ConnectionRecord"/>.</summary>
public sealed class ConnectionRowViewModel
{
    public ConnectionRowViewModel(ConnectionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        Key = $"{record.Protocol}|{record.Local}|{record.Remote}|{record.ProcessId}";
        ProcessId = record.ProcessId;
        FullPath = record.ExecutablePath;
        Protocol = record.Protocol == TransportProtocol.Tcp ? "TCP" : "UDP";
        IsInternet = record.IsRemoteInternet;

        // Three different reasons a name can be missing, and conflating them misleads: a closing
        // socket has genuinely lost its owner, the kernel has no user-mode image, and everything
        // else is this process lacking the rights to look.
        // Path is fully qualified because WPF drops System.IO from implicit usings, to avoid
        // colliding with System.Windows.Shapes.Path.
        AppName = record switch
        {
            { ProcessId: 0 } => "(tiến trình đã kết thúc)",
            { ProcessId: 4 } => "System",
            { ExecutablePath: null } => "(không đọc được — cần Admin)",
            var r => System.IO.Path.GetFileName(r.ExecutablePath!),
        };

        Destination = record.Remote is null ? "—" : record.Remote.Address.ToString();
        Port = record.Remote?.Port.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—";
        State = record.State?.ToString() ?? "—";
        Local = record.Local.ToString();
    }

    /// <summary>Identity used to sync the grid between polls without rebuilding it.</summary>
    public string Key { get; }

    public string AppName { get; }
    public string? FullPath { get; }
    public int ProcessId { get; }
    public string Protocol { get; }
    public string Destination { get; }
    public string Port { get; }
    public string State { get; }
    public string Local { get; }
    public bool IsInternet { get; }

    /// <summary>Tooltip text; falls back to the reason the path is missing.</summary>
    public string PathTooltip => FullPath ?? "Không mở được process để lấy đường dẫn.";
}
