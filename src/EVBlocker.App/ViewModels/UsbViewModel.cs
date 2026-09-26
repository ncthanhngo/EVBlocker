using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using EVBlocker.App.Mvvm;
using EVBlocker.Core.Usb;

namespace EVBlocker.App.ViewModels;

/// <summary>One finding, as the grid shows it.</summary>
public sealed class UsbFindingRowViewModel
{
    public UsbFindingRowViewModel(UsbFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        Source = finding;
    }

    public UsbFinding Source { get; }

    public string Path => Source.Path;

    public string Name => System.IO.Path.GetFileName(Source.Path);

    public string Reason => Source.Reason;

    public string Level => Source.Confidence switch
    {
        ThreatConfidence.Certain => "Chắc chắn là virus",
        ThreatConfidence.Suspicious => "Đáng ngờ",
        _ => "Chỉ bị ẩn",
    };

    /// <summary>What will happen to it, so nobody presses the button without knowing.</summary>
    public string Action => Source.Quarantine ? "Sẽ chuyển vào khu cách ly" : "Sẽ cho hiện lại";

    public bool IsThreat => Source.Confidence != ThreatConfidence.Notice;
}

/// <summary>
/// Scans drives that came from outside the machine, and cleans what it finds.
/// </summary>
/// <remarks>
/// Scanning is automatic when a drive appears; cleaning never is. Moving files off someone's
/// drive is not something to do behind their back, however sure the rules are.
/// </remarks>
public sealed class UsbViewModel : ObservableObject
{
    private readonly IRemovableDriveProbe _probe;
    private readonly IUsbScanner _scanner;
    private readonly Func<UsbQuarantine> _quarantineFactory;
    private readonly Func<string, string, bool> _confirm;
    private readonly RelayCommand _scanCommand;
    private readonly RelayCommand _cleanCommand;

    private string _status = "Chưa quét.";
    private bool _isBusy;

    public UsbViewModel(
        IRemovableDriveProbe probe,
        IUsbScanner scanner,
        Func<UsbQuarantine> quarantineFactory,
        Func<string, string, bool> confirm)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(quarantineFactory);
        ArgumentNullException.ThrowIfNull(confirm);

        _probe = probe;
        _scanner = scanner;
        _quarantineFactory = quarantineFactory;
        _confirm = confirm;

        _scanCommand = new RelayCommand(() => _ = ScanAsync(), () => !_isBusy);
        _cleanCommand = new RelayCommand(Clean, () => !_isBusy && Rows.Count > 0);

        _ = ScanAsync();
    }

    public ObservableCollection<UsbFindingRowViewModel> Rows { get; } = new();

    public System.Windows.Input.ICommand ScanCommand => _scanCommand;

    public System.Windows.Input.ICommand CleanCommand => _cleanCommand;

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>Called when Windows reports a drive arriving or leaving.</summary>
    public void OnDrivesChanged() => _ = ScanAsync();

    private async Task ScanAsync()
    {
        _isBusy = true;
        RefreshCommands();
        Status = "Đang quét…";

        IReadOnlyList<UsbScanResult> results = await Task
            .Run(() => _probe.List().Select(_scanner.Scan).ToList())
            .ConfigureAwait(true);

        Rows.Clear();
        foreach (UsbFinding finding in results.SelectMany(r => r.Findings))
        {
            Rows.Add(new UsbFindingRowViewModel(finding));
        }

        _isBusy = false;
        RefreshCommands();
        Status = Describe(results);
    }

    private string Describe(IReadOnlyList<UsbScanResult> results)
    {
        if (results.Count == 0)
        {
            return "Không có ổ USB nào đang cắm.";
        }

        string drives = string.Join(" · ", results.Select(r => $"{r.Drive.Root} ({r.Drive.Label})"));
        int threats = Rows.Count(r => r.IsThreat);
        int hidden = Rows.Count - threats;
        int unreadable = results.Sum(r => r.DirectoriesUnreadable);

        // An unreadable directory is not a clean one, so it is never left out of the count.
        string unread = unreadable > 0
            ? $" · {unreadable} thư mục không đọc được"
            : string.Empty;

        if (threats == 0 && hidden == 0)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{drives} — sạch{unread}.");
        }

        return string.Create(
            CultureInfo.CurrentCulture,
            $"{drives} — {threats} dấu hiệu virus, {hidden} thư mục bị ẩn{unread}.");
    }

    private void Clean()
    {
        int threats = Rows.Count(r => r.IsThreat);
        int hidden = Rows.Count - threats;

        if (!_confirm(
                "Dọn ổ USB?",
                $"{threats} file nghi là virus sẽ được chuyển vào khu cách ly, và {hidden} thư mục "
                + "bị ẩn sẽ được cho hiện lại.\n\nFile bị cách ly KHÔNG bị xoá — chúng được chuyển "
                + "sang thư mục của ứng dụng kèm ghi chú nơi lấy ra, nên lấy lại được nếu nhầm."
                + "\n\nTiếp tục?"))
        {
            return;
        }

        CleanupResult result = _quarantineFactory().Apply(Rows.Select(r => r.Source).ToList());

        string failures = result.Failures.Count > 0
            ? $" · {result.Failures.Count} mục không xử lý được"
            : string.Empty;

        Status = string.Create(
            CultureInfo.CurrentCulture,
            $"Đã cách ly {result.Quarantined} file, cho hiện lại {result.FoldersRestored} thư mục{failures}.");

        // Re-scanned rather than assumed: the result of the clean is whatever the drive says now.
        _ = ScanAsync();
    }

    private void RefreshCommands()
    {
        _scanCommand.RaiseCanExecuteChanged();
        _cleanCommand.RaiseCanExecuteChanged();
    }
}
