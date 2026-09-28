using System.Collections.ObjectModel;
using EVBlocker.App.Mvvm;
using EVBlocker.App.Services;
using EVBlocker.Core.Audit;
using EVBlocker.Core.Firewall;
using EVBlocker.Core.History;
using EVBlocker.Core.Installed;
using EVBlocker.Core.Monitor;
using EVBlocker.Core.Policy;
using EVBlocker.Core.Startup;
using EVBlocker.Core.Usb;

namespace EVBlocker.App.ViewModels;

/// <summary>Shell view model: navigation rail, status strip and the elevation banner.</summary>
public sealed class MainViewModel : ObservableObject
{
    private NavSectionViewModel _selectedSection;

    public MainViewModel()
    {
        // Dot colours come from the shared palette's nav set, so a section here reads as the
        // same kind of thing as a section in the other EVSELab apps.
        AllowListStore store = CoreServices.CreateAllowListStore();

        AllowList = new AllowListViewModel(
            store,
            () => new WindowsFirewallPolicy(),
            PickExecutables,
            PickRunningApps,
            PickInstalledApps,
            Confirm);

        ActiveConnections = new ActiveConnectionsViewModel(new ActiveConnectionScanner(), CreateAccessActions());

        // Built per read rather than held: the device map inside it refreshes on a miss, and a
        // long-lived instance would keep a map from whenever the app happened to start.
        History = new HistoryViewModel(
            () => new WfpEventLogReader(new DevicePathMapper()),
            () => new AuditPolicyManager(),
            CreateAccessActions());

        Monitor = new MonitorViewModel(ActiveConnections, History);

        Usb = new UsbViewModel(
            new RemovableDriveProbe(),
            new UsbScanner(),
            () => new UsbQuarantine(),
            Confirm);

        Enforcement = new EnforcementViewModel(
            CoreServices.CreateEnforcementController(),
            store,
            new StartupReconcileTask(),
            Confirm);

        Settings = new SettingsViewModel();

        // The allow-list leads and opens by default: the question people come with is which
        // software may reach the internet, and watching what it does is secondary to that.
        Sections = new ObservableCollection<NavSectionViewModel>
        {
            new("Phần mềm được phép", "#2BD673", AllowList),
            new("Theo dõi", "#5AA9FF", Monitor),
            new("USB", "#F5B93B", Usb),
        };

        // Its own list, pinned to the foot of the rail. Settings is not a peer of the two above
        // it - it configures the application rather than the policy - and the convention of
        // putting it last is strong enough that breaking it costs more than it gains.
        BottomSections = new ObservableCollection<NavSectionViewModel>
        {
            new("Cài đặt", "#94A3B8", Settings),
        };

        _selectedSection = Sections[0];
        RelaunchElevatedCommand = new RelayCommand(RelaunchElevated, () => !ElevationService.IsElevated);
    }

    public ObservableCollection<NavSectionViewModel> Sections { get; }

    public ObservableCollection<NavSectionViewModel> BottomSections { get; }

    /// <summary>
    /// The section on screen.
    /// </summary>
    /// <remarks>
    /// Null is ignored on purpose. The rail is two lists, so whichever one did not make the
    /// selection clears its own and writes null back; taking that literally would blank the
    /// window every time the user moved between the two halves.
    /// </remarks>
    public NavSectionViewModel SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (value is not null)
            {
                SetProperty(ref _selectedSection, value);
            }
        }
    }

    public ActiveConnectionsViewModel ActiveConnections { get; }

    public HistoryViewModel History { get; }

    public AllowListViewModel AllowList { get; }

    public MonitorViewModel Monitor { get; }

    public UsbViewModel Usb { get; }

    public EnforcementViewModel Enforcement { get; }

    public SettingsViewModel Settings { get; }

    public System.Windows.Input.ICommand RelaunchElevatedCommand { get; }

    public bool ShowElevationBanner => !ElevationService.IsElevated;

    public string ElevationMessage =>
        "Đang chạy với quyền thường — sẽ không thấy đường dẫn của phần lớn tiến trình, "
        + "và không đọc được nhật ký của Windows.";

    /// <summary>
    /// Shown in the rail. Replaced a hard-coded phase label that went stale every time a phase
    /// landed, and that meant nothing to anyone running the app.
    /// </summary>
    public static string AppVersion =>
        $"EVBlocker {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0"}";

    public string ElevationBadge => ElevationService.IsElevated ? "Quyền quản trị" : "Quyền thường";

    /// <summary>
    /// The two WPF-facing pieces the allow-list needs. Passed in as delegates so that view model
    /// stays free of any reference to a dialog or a window.
    /// </summary>
    private static string[]? PickExecutables()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Chọn phần mềm được phép ra internet",
            Filter = "Ứng dụng (*.exe)|*.exe",
            Multiselect = true,
            CheckFileExists = true,
        };

        return dialog.ShowDialog() == true ? dialog.FileNames : null;
    }

    /// <summary>
    /// Opens the scan dialog and returns what the user ticked, or null if they cancelled.
    /// </summary>
    /// <remarks>
    /// Owned by the main window so the dialog is modal to it and centres on it; without an owner
    /// a modal dialog can end up behind the window it belongs to.
    /// </remarks>
    private static IReadOnlyList<ScannedSelection>? PickRunningApps()
    {
        var window = new Views.ScanWindow(
            new ScanViewModel(
                new RunningAppScanner(),
                () => CoreServices.CreateAllowListStore().Load().Apps
                    .Select(a => a.ExecutablePath)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)))
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };

        return window.ShowDialog() == true
            ? window.Selected
                .Select(a => new ScannedSelection(a.Name, a.ExecutablePath))
                .ToList()
            : null;
    }

    /// <summary>Opens the installed-programs picker and returns what was ticked.</summary>
    private static IReadOnlyList<ScannedSelection>? PickInstalledApps()
    {
        var window = new Views.InstalledProgramsWindow(
            new InstalledProgramsViewModel(
                new InstalledProgramScanner(),
                () => CoreServices.CreateAllowListStore().Load().Apps
                    .Select(a => a.ExecutablePath)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)))
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };

        return window.ShowDialog() == true ? window.Selected : null;
    }

    /// <summary>
    /// The right-click choices on the monitoring page, both ending in the allow-list.
    /// </summary>
    /// <remarks>
    /// Revoking cuts the program's open connections as well, after the firewall has been written,
    /// so that its reconnect is the attempt that gets refused. That only makes sense while
    /// blocking is on - with it off every program reaches the internet whatever the list says -
    /// so then the result says so instead of cutting connections that would come straight back.
    /// </remarks>
    private AppAccessActions CreateAccessActions() => new(
        (path, name) => AllowList.AllowNow(path, name),
        path =>
        {
            (bool applied, string message) = AllowList.RevokeNow(path);

            if (Enforcement.State == EnforcementState.Off)
            {
                return message + " Lưu ý: chặn đang tắt nên nó vẫn ra được internet cho tới khi bật chặn.";
            }

            if (!applied)
            {
                return message;
            }

            CloseResult cut = new TcpConnectionCloser(new ActiveConnectionScanner()).CloseAll(path);
            return cut switch
            {
                { Failed: > 0 } => message + $" Không cắt được {cut.Failed} kết nối đang mở.",
                { Closed: > 0 } => message + $" Đã cắt {cut.Closed} kết nối đang mở.",
                _ => message,
            };
        },
        path => AllowList.IsAllowed(path));

    private static bool Confirm(string title, string message) =>
        System.Windows.MessageBox.Show(
            message,
            title,
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;

    private static void RelaunchElevated()
    {
        if (ElevationService.TryRelaunchElevated())
        {
            App.ExitApplication();
        }
    }
}
