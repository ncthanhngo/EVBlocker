using System.Collections.ObjectModel;
using EVBlocker.App.Mvvm;
using EVBlocker.App.Services;
using EVBlocker.Core.Firewall;
using EVBlocker.Core.History;
using EVBlocker.Core.Monitor;
using EVBlocker.Core.Policy;
using EVBlocker.Core.Startup;

namespace EVBlocker.App.ViewModels;

/// <summary>Shell view model: navigation rail, status strip and the elevation banner.</summary>
public sealed class MainViewModel : ObservableObject
{
    private NavSectionViewModel _selectedSection;

    public MainViewModel()
    {
        ActiveConnections = new ActiveConnectionsViewModel(new ActiveConnectionScanner());

        // Built per read rather than held: the device map inside it refreshes on a miss, and a
        // long-lived instance would keep a map from whenever the app happened to start.
        History = new HistoryViewModel(() => new WfpEventLogReader(new DevicePathMapper()));

        // Dot colours come from the shared palette's nav set, so a section here reads as the
        // same kind of thing as a section in the other EVSELab apps.
        AllowListStore store = CoreServices.CreateAllowListStore();

        AllowList = new AllowListViewModel(
            store,
            () => new WindowsFirewallPolicy(),
            PickExecutables,
            Confirm);

        Enforcement = new EnforcementViewModel(
            CoreServices.CreateEnforcementController(),
            store,
            new StartupReconcileTask(),
            Confirm);

        Settings = new SettingsViewModel();

        Sections = new ObservableCollection<NavSectionViewModel>
        {
            new("Đang kết nối", "#5AA9FF", ActiveConnections),
            new("Đã thử kết nối", "#F5B93B", History),
            new("Allow-list", "#2BD673", AllowList),
            new("Cài đặt", "#94A3B8", Settings),
        };

        _selectedSection = Sections[0];
        RelaunchElevatedCommand = new RelayCommand(RelaunchElevated, () => !ElevationService.IsElevated);
    }

    public ObservableCollection<NavSectionViewModel> Sections { get; }

    public NavSectionViewModel SelectedSection
    {
        get => _selectedSection;
        set => SetProperty(ref _selectedSection, value);
    }

    public ActiveConnectionsViewModel ActiveConnections { get; }

    public HistoryViewModel History { get; }

    public AllowListViewModel AllowList { get; }

    public EnforcementViewModel Enforcement { get; }

    public SettingsViewModel Settings { get; }

    public System.Windows.Input.ICommand RelaunchElevatedCommand { get; }

    public bool ShowElevationBanner => !ElevationService.IsElevated;

    public string ElevationMessage =>
        "Đang chạy không có quyền Admin — đường dẫn của phần lớn tiến trình sẽ trống "
        + "và không đọc được lịch sử từ Security log.";

    /// <summary>
    /// Shown in the rail. Replaced a hard-coded phase label that went stale every time a phase
    /// landed, and that meant nothing to anyone running the app.
    /// </summary>
    public static string AppVersion =>
        $"EVBlocker {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0"}";

    public string ElevationBadge => ElevationService.IsElevated ? "Administrator" : "Quyền hạn chế";

    /// <summary>
    /// The two WPF-facing pieces the allow-list needs. Passed in as delegates so that view model
    /// stays free of any reference to a dialog or a window.
    /// </summary>
    private static string[]? PickExecutables()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Chọn ứng dụng được phép ra internet",
            Filter = "Ứng dụng (*.exe)|*.exe",
            Multiselect = true,
            CheckFileExists = true,
        };

        return dialog.ShowDialog() == true ? dialog.FileNames : null;
    }

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
            System.Windows.Application.Current.Shutdown();
        }
    }
}
