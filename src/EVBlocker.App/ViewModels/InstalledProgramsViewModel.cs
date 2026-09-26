using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using EVBlocker.App.Mvvm;
using EVBlocker.Core.Installed;

namespace EVBlocker.App.ViewModels;

/// <summary>One installed program, with a tick box.</summary>
public sealed class InstalledProgramRowViewModel : ObservableObject
{
    private bool _isSelected;

    public InstalledProgramRowViewModel(InstalledProgram program, IReadOnlySet<string> allowed)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(allowed);

        Source = program;
        AllowedCount = program.Executables.Count(allowed.Contains);
    }

    public InstalledProgram Source { get; }

    /// <summary>How many of this program's executables are already in the allow-list.</summary>
    public int AllowedCount { get; }

    public string Name => Source.Name;

    public string Publisher => Source.Publisher ?? "—";

    public string Version => Source.Version ?? "—";

    public string InstalledOn => Source.InstalledOn is { } date
        ? date.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture)
        : "—";

    /// <summary>The executable shown in the row, and the one its icon comes from.</summary>
    public string? FirstExecutable => Source.Executables.FirstOrDefault();

    public bool IsResolved => Source.Executables.Count > 0;

    /// <summary>
    /// Only a program with at least one executable, and not already fully allowed, can be ticked.
    /// </summary>
    public bool CanSelect => IsResolved && AllowedCount < Source.Executables.Count;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string Status => Source.Executables.Count switch
    {
        0 => "Không tìm được file chạy",
        var count when AllowedCount == count => "Đã cho phép",
        var count when AllowedCount > 0 => $"Cho phép {AllowedCount}/{count}",
        1 => "Chưa quyết định",
        var count => $"Chưa quyết định · {count} file",
    };

    /// <summary>Full list of paths, so a row covering several files can be checked before ticking.</summary>
    public string PathTooltip => Source.Executables.Count == 0
        ? "Windows không cho biết file chạy của phần mềm này. Dùng nút \"Thêm ứng dụng…\" ở mục Danh sách cho phép."
        : string.Join("\n", Source.Executables);
}

/// <summary>
/// Shows what is installed on this machine, the way Control Panel's Programs page does, and
/// turns a tick into an allow-list entry.
/// </summary>
/// <remarks>
/// Read on demand rather than held, so software installed since the app started is simply there
/// the next time the section is opened - there is nothing to refresh and nothing to keep in sync.
/// </remarks>
public sealed class InstalledProgramsViewModel : ObservableObject
{
    private readonly IInstalledProgramScanner _scanner;
    private readonly Func<IReadOnlySet<string>> _allowedPaths;
    private readonly Func<IReadOnlyList<ScannedSelection>, int> _allow;
    private readonly RelayCommand _refreshCommand;
    private readonly RelayCommand _allowCommand;

    private IReadOnlyList<InstalledProgramRowViewModel> _all = Array.Empty<InstalledProgramRowViewModel>();
    private bool _hideSupportComponents = true;
    private bool _undecidedOnly;
    private string _searchText = string.Empty;
    private string _status = "Đang đọc danh sách phần mềm…";
    private bool _isBusy;

    public InstalledProgramsViewModel(
        IInstalledProgramScanner scanner,
        Func<IReadOnlySet<string>> allowedPaths,
        Func<IReadOnlyList<ScannedSelection>, int> allow)
    {
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(allowedPaths);
        ArgumentNullException.ThrowIfNull(allow);

        _scanner = scanner;
        _allowedPaths = allowedPaths;
        _allow = allow;

        _refreshCommand = new RelayCommand(() => _ = LoadAsync(), () => !_isBusy);
        _allowCommand = new RelayCommand(AllowSelected, () => !_isBusy);

        _ = LoadAsync();
    }

    public ObservableCollection<InstalledProgramRowViewModel> Rows { get; } = new();

    public System.Windows.Input.ICommand RefreshCommand => _refreshCommand;

    public System.Windows.Input.ICommand AllowCommand => _allowCommand;

    /// <summary>Hides drivers, runtimes and redistributables, which are most of the list.</summary>
    public bool HideSupportComponents
    {
        get => _hideSupportComponents;
        set
        {
            if (SetProperty(ref _hideSupportComponents, value))
            {
                ApplyFilter();
            }
        }
    }

    /// <summary>Shows only what has not been decided yet - where newly installed software lands.</summary>
    public bool UndecidedOnly
    {
        get => _undecidedOnly;
        set
        {
            if (SetProperty(ref _undecidedOnly, value))
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

    private async Task LoadAsync()
    {
        _isBusy = true;
        RefreshCommands();
        Status = "Đang đọc danh sách phần mềm trên máy…";

        IReadOnlySet<string> allowed = _allowedPaths();

        // Off the UI thread: the scan walks three registry roots and stats the directories they
        // name, and a stalled window during that read would be the first thing anyone noticed.
        IReadOnlyList<InstalledProgram> programs = await Task.Run(_scanner.Scan).ConfigureAwait(true);

        _all = programs
            .Select(program => new InstalledProgramRowViewModel(program, allowed))
            .ToList();

        _isBusy = false;
        ApplyFilter();
        RefreshCommands();
    }

    private void ApplyFilter()
    {
        string search = _searchText.Trim();

        List<InstalledProgramRowViewModel> visible = _all
            .Where(row => !_hideSupportComponents || !row.Source.IsSupportComponent)
            .Where(row => !_undecidedOnly || row.AllowedCount == 0)
            .Where(row => search.Length == 0
                || row.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || (row.FirstExecutable?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

        Rows.Clear();
        foreach (InstalledProgramRowViewModel row in visible)
        {
            Rows.Add(row);
        }

        int hidden = _all.Count - visible.Count;
        int undecided = _all.Count(row => row.AllowedCount == 0 && row.IsResolved && !row.Source.IsSupportComponent);

        Status = string.Create(
            CultureInfo.CurrentCulture,
            $"{_all.Count} phần mềm · {undecided} chưa quyết định{(hidden > 0 ? $" · {hidden} đang ẩn" : string.Empty)}");
    }

    private void AllowSelected()
    {
        List<ScannedSelection> picked = Rows
            .Where(row => row.IsSelected && row.CanSelect)
            .SelectMany(row => row.Source.Executables.Select(path => new ScannedSelection(row.Name, path)))
            .ToList();

        if (picked.Count == 0)
        {
            Status = "Chưa tích phần mềm nào.";
            return;
        }

        int added = _allow(picked);

        Status = added == 0
            ? "Những file đó đã được cho phép rồi."
            : $"Đã cho phép {added} file. Sang mục Danh sách cho phép rồi bấm \"Lưu vào tường lửa\" để áp dụng.";

        // Reloaded rather than patched: the allow-list is the source of truth for what a row
        // says, and it has just changed underneath every row, not only the ticked ones.
        _ = LoadAsync();
    }

    private void RefreshCommands()
    {
        _refreshCommand.RaiseCanExecuteChanged();
        _allowCommand.RaiseCanExecuteChanged();
    }
}
