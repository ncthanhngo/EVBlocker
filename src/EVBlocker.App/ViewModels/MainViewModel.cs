using System.Collections.ObjectModel;
using EVBlocker.App.Mvvm;
using EVBlocker.App.Services;
using EVBlocker.Core.History;
using EVBlocker.Core.Monitor;

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
        Sections = new ObservableCollection<NavSectionViewModel>
        {
            new("Đang kết nối", "#5AA9FF", ActiveConnections),
            new("Đã thử kết nối", "#F5B93B", History),
            new(
                "Allow-list",
                "#2BD673",
                new PlaceholderViewModel(
                    "Chưa khả dụng",
                    "Danh sách ứng dụng được phép ra internet sẽ có ở Phase 02, cùng engine tạo rule trong Windows Firewall.")),
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

    public System.Windows.Input.ICommand RelaunchElevatedCommand { get; }

    public bool ShowElevationBanner => !ElevationService.IsElevated;

    public string ElevationMessage =>
        "Đang chạy không có quyền Admin — đường dẫn của phần lớn tiến trình sẽ trống "
        + "và không đọc được lịch sử từ Security log.";

    /// <summary>
    /// Enforcement is not implemented yet. Stated plainly so the status strip cannot be read as
    /// "blocking is available but switched off".
    /// </summary>
    public string EnforcementStatus => "Chưa khả dụng · Phase 04";

    public string AuditStatus => ElevationService.IsElevated
        ? "Cần bật để có dữ liệu lịch sử"
        : "Không kiểm tra được · cần Admin";

    public string ElevationBadge => ElevationService.IsElevated ? "Administrator" : "Quyền hạn chế";

    private static void RelaunchElevated()
    {
        if (ElevationService.TryRelaunchElevated())
        {
            System.Windows.Application.Current.Shutdown();
        }
    }
}
