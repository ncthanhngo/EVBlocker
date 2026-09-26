using System.Collections.ObjectModel;
using System.Globalization;
using EVBlocker.App.Mvvm;
using EVBlocker.App.Services;
using EVBlocker.Core.Monitor;

namespace EVBlocker.App.ViewModels;

/// <summary>One running executable, with a tick box.</summary>
public sealed class ScanRowViewModel : ObservableObject
{
    private bool _isSelected;

    public ScanRowViewModel(RunningApp app, bool alreadyAllowed)
    {
        ArgumentNullException.ThrowIfNull(app);

        Source = app;
        AlreadyAllowed = alreadyAllowed;

        // Pre-ticked when it is already talking to the internet and is not a Windows component:
        // those are the ones a person scanning their machine means to keep working. Nothing is
        // added until they press the button, so a suggestion costs nothing.
        _isSelected = !alreadyAllowed && app.HasInternetConnection && !app.IsWindowsComponent;
    }

    public RunningApp Source { get; }

    public bool AlreadyAllowed { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>Already-allowed rows are shown but cannot be ticked; there is nothing to add.</summary>
    public bool CanSelect => !AlreadyAllowed;

    public string Name => Source.Name;

    public string ExecutablePath => Source.ExecutablePath;

    public string ProcessCountText => Source.ProcessCount == 1
        ? "1 tiến trình"
        : $"{Source.ProcessCount} tiến trình";

    public string Status => AlreadyAllowed
        ? "Đã cho phép"
        : Source.HasInternetConnection ? "Đang ra internet" : string.Empty;

    public bool IsWindowsComponent => Source.IsWindowsComponent;
}

/// <summary>Drives the scan dialog: list what is running, let the user pick.</summary>
public sealed class ScanViewModel : ObservableObject
{
    private readonly IRunningAppScanner _scanner;
    private readonly Func<IReadOnlySet<string>> _allowedPaths;
    private readonly RelayCommand _rescanCommand;

    private List<ScanRowViewModel> _all = new();
    private bool _hideWindowsComponents = true;
    private bool _onlyConnected;
    private string _searchText = string.Empty;
    private string _status = string.Empty;

    public ScanViewModel(IRunningAppScanner scanner, Func<IReadOnlySet<string>> allowedPaths)
    {
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(allowedPaths);

        _scanner = scanner;
        _allowedPaths = allowedPaths;
        _rescanCommand = new RelayCommand(Rescan);

        Rescan();
    }

    public ObservableCollection<ScanRowViewModel> Rows { get; } = new();

    public System.Windows.Input.ICommand RescanCommand => _rescanCommand;

    /// <summary>
    /// On by default. Allow-listing an executable under the Windows directory is almost always
    /// wrong, and the shared hosts there are already covered by the baseline, scoped by service.
    /// </summary>
    public bool HideWindowsComponents
    {
        get => _hideWindowsComponents;
        set
        {
            if (SetProperty(ref _hideWindowsComponents, value))
            {
                ApplyFilter();
            }
        }
    }

    public bool OnlyConnected
    {
        get => _onlyConnected;
        set
        {
            if (SetProperty(ref _onlyConnected, value))
            {
                ApplyFilter();
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
                ApplyFilter();
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>Paths the user ticked. Read by the dialog's caller once it closes with OK.</summary>
    public IReadOnlyList<RunningApp> Selected => _all
        .Where(r => r.IsSelected && r.CanSelect)
        .Select(r => r.Source)
        .ToList();

    private void Rescan()
    {
        RunningAppScan scan = _scanner.Scan();
        IReadOnlySet<string> allowed = _allowedPaths();

        _all = scan.Apps
            .Select(app => new ScanRowViewModel(app, allowed.Contains(app.ExecutablePath)))
            .ToList();

        // The unreadable count is the honest part. Without elevation it is most of the machine,
        // and a list that looked complete would be a bad basis for deciding what to block.
        string coverage = string.Create(
            CultureInfo.CurrentCulture,
            $"Đọc được {scan.ProcessesSeen - scan.ProcessesUnreadable}/{scan.ProcessesSeen} tiến trình");

        Status = scan.ProcessesUnreadable > 0 && !ElevationService.IsElevated
            ? $"{coverage}. Chạy với quyền Admin để thấy đầy đủ."
            : $"{coverage}.";

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Rows.Clear();

        foreach (ScanRowViewModel row in _all.Where(Matches))
        {
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(Selected));
    }

    private bool Matches(ScanRowViewModel row)
    {
        if (_hideWindowsComponents && row.IsWindowsComponent)
        {
            return false;
        }

        if (_onlyConnected && !row.Source.HasInternetConnection)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_searchText))
        {
            return true;
        }

        string needle = _searchText.Trim();

        return row.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
               || row.ExecutablePath.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }
}
