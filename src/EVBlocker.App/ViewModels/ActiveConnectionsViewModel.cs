using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using EVBlocker.App.Mvvm;
using EVBlocker.Core.Monitor;

namespace EVBlocker.App.ViewModels;

/// <summary>Polls the live socket tables and keeps the grid in sync.</summary>
public sealed class ActiveConnectionsViewModel : ObservableObject
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly IConnectionScanner _scanner;
    private readonly DispatcherTimer _timer;

    /// <summary>
    /// TCP states that mean the connection is finished and only the socket is lingering.
    /// A closed browser tab can leave dozens of these to one address, burying the live rows.
    /// </summary>
    private static readonly HashSet<TcpConnectionState> ClosingStates = new()
    {
        TcpConnectionState.TimeWait,
        TcpConnectionState.CloseWait,
        TcpConnectionState.Closing,
        TcpConnectionState.Closed,
        TcpConnectionState.FinWait1,
        TcpConnectionState.FinWait2,
        TcpConnectionState.LastAck,
        TcpConnectionState.DeleteTcb,
    };

    private bool _internetOnly = true;
    private bool _hideClosing = true;
    private string _searchText = string.Empty;
    private string _summary = "Đang tải…";

    public ActiveConnectionsViewModel(IConnectionScanner scanner)
    {
        ArgumentNullException.ThrowIfNull(scanner);
        _scanner = scanner;

        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += (_, _) => Refresh();
    }

    public ObservableCollection<ConnectionRowViewModel> Rows { get; } = new();

    /// <summary>
    /// On by default: the product is about internet access, and unfiltered the grid is mostly
    /// listeners and loopback that no allow-list decision depends on.
    /// </summary>
    public bool InternetOnly
    {
        get => _internetOnly;
        set
        {
            if (SetProperty(ref _internetOnly, value))
            {
                Refresh();
            }
        }
    }

    /// <summary>On by default: lingering sockets are noise for an allow-list decision.</summary>
    public bool HideClosing
    {
        get => _hideClosing;
        set
        {
            if (SetProperty(ref _hideClosing, value))
            {
                Refresh();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                Refresh();
            }
        }
    }

    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public void Start()
    {
        Refresh();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    private void Refresh()
    {
        IReadOnlyList<ConnectionRecord> snapshot = _scanner.Scan();

        List<ConnectionRowViewModel> desired = snapshot
            .Where(Matches)
            .Select(record => new ConnectionRowViewModel(record))
            .OrderBy(row => row.AppName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.Destination, StringComparer.Ordinal)
            .ToList();

        Sync(desired);

        int internetCount = snapshot.Count(record => record.IsRemoteInternet);
        Summary = string.Create(
            CultureInfo.CurrentCulture,
            $"{snapshot.Count} socket · {internetCount} ra internet · hiện {Rows.Count} dòng · cập nhật {DateTime.Now:HH:mm:ss}");
    }

    private bool Matches(ConnectionRecord record)
    {
        if (_internetOnly && !record.IsRemoteInternet)
        {
            return false;
        }

        if (_hideClosing && record.State is { } state && ClosingStates.Contains(state))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_searchText))
        {
            return true;
        }

        string needle = _searchText.Trim();

        return (record.ExecutablePath?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false)
               || (record.Remote?.ToString().Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false)
               || record.ProcessId.ToString(CultureInfo.InvariantCulture).Contains(needle, StringComparison.Ordinal);
    }

    /// <summary>
    /// Applies the difference instead of clearing and refilling.
    /// </summary>
    /// <remarks>
    /// Clear-then-add at one-second intervals destroys the user's selection and scroll position
    /// every tick, and makes the DataGrid rebuild every container. Syncing by key leaves
    /// unchanged rows - almost all of them - untouched.
    /// </remarks>
    private void Sync(List<ConnectionRowViewModel> desired)
    {
        var desiredByKey = new Dictionary<string, ConnectionRowViewModel>(desired.Count, StringComparer.Ordinal);
        foreach (ConnectionRowViewModel row in desired)
        {
            // Two identical tuples can legitimately appear (several sockets, same 4-tuple view);
            // the first wins and the duplicate is dropped from the display.
            desiredByKey.TryAdd(row.Key, row);
        }

        for (int i = Rows.Count - 1; i >= 0; i--)
        {
            if (!desiredByKey.ContainsKey(Rows[i].Key))
            {
                Rows.RemoveAt(i);
            }
        }

        var presentKeys = new HashSet<string>(Rows.Select(row => row.Key), StringComparer.Ordinal);

        for (int i = 0; i < desired.Count; i++)
        {
            ConnectionRowViewModel row = desired[i];

            if (presentKeys.Add(row.Key))
            {
                // Insert at the sorted position so ordering stays stable without a full resort.
                int index = Math.Min(i, Rows.Count);
                Rows.Insert(index, row);
            }
        }
    }
}
