using System.Collections.ObjectModel;
using System.Globalization;
// WPF leaves System.IO out of its implicit usings because System.Windows.Shapes.Path would
// collide with System.IO.Path. Imported here for File and the stream types; Path stays
// fully qualified at its use sites.
using System.IO;
using System.Security.Cryptography;
using EVBlocker.App.Mvvm;
using EVBlocker.App.Services;
using EVBlocker.Core.Firewall;
using EVBlocker.Core.Policy;

namespace EVBlocker.App.ViewModels;

/// <summary>One allowed executable, as the grid shows it.</summary>
public sealed class AllowedAppRowViewModel
{
    public AllowedAppRowViewModel(AllowedApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        Source = app;
        DisplayName = string.IsNullOrWhiteSpace(app.DisplayName)
            ? System.IO.Path.GetFileName(app.ExecutablePath)
            : app.DisplayName;
        ExecutablePath = app.ExecutablePath;
        AddedAt = app.AddedAt == default
            ? "—"
            : app.AddedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);

        // Only an existence check here. Comparing the recorded hash would mean reading every
        // allowed executable on the UI thread each time the list refreshes, which is exactly the
        // kind of stall the rest of this app avoids.
        FileMissing = !File.Exists(app.ExecutablePath);
        Status = FileMissing ? "Không tìm thấy file" : "OK";
    }

    public AllowedApp Source { get; }

    public string DisplayName { get; }
    public string ExecutablePath { get; }
    public string AddedAt { get; }
    public string Status { get; }
    public bool FileMissing { get; }
}

/// <summary>Manages the allow-list and pushes it into Windows Firewall.</summary>
public sealed class AllowListViewModel : ObservableObject
{
    /// <summary>
    /// Allowing any of these allows everything that can be run through them. A script host with
    /// outbound access is an outbound allowance for every script on the machine, so the user is
    /// told before it is added rather than after something surprising happens.
    /// </summary>
    private static readonly HashSet<string> Interpreters = new(StringComparer.OrdinalIgnoreCase)
    {
        "powershell.exe", "pwsh.exe", "cmd.exe", "wscript.exe", "cscript.exe", "mshta.exe",
        "rundll32.exe", "regsvr32.exe", "curl.exe", "bitsadmin.exe", "certutil.exe",
        "python.exe", "node.exe", "java.exe", "ssh.exe",
    };

    private readonly AllowListStore _store;
    private readonly Func<IFirewallPolicy> _policyFactory;
    private readonly Func<string[]?> _pickFiles;
    private readonly Func<string, string, bool> _confirm;

    private readonly RelayCommand _addCommand;
    private readonly RelayCommand _addCommonCommand;
    private readonly RelayCommand _removeCommand;
    private readonly RelayCommand _applyCommand;

    private AllowListDocument _document = new();
    private AllowedAppRowViewModel? _selected;
    private string _status = string.Empty;
    private bool _loadFailed;

    /// <summary>
    /// The file dialog and the confirmation prompt arrive as delegates so this view model holds
    /// no reference to WPF, and so its branching can be exercised without a window.
    /// </summary>
    public AllowListViewModel(
        AllowListStore store,
        Func<IFirewallPolicy> policyFactory,
        Func<string[]?> pickFiles,
        Func<string, string, bool> confirm)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(policyFactory);
        ArgumentNullException.ThrowIfNull(pickFiles);
        ArgumentNullException.ThrowIfNull(confirm);

        _store = store;
        _policyFactory = policyFactory;
        _pickFiles = pickFiles;
        _confirm = confirm;

        _addCommand = new RelayCommand(Add, () => !_loadFailed);
        _addCommonCommand = new RelayCommand(AddCommonApps, () => !_loadFailed);
        _removeCommand = new RelayCommand(RemoveSelected, () => _selected is not null && !_loadFailed);
        _applyCommand = new RelayCommand(ApplyToFirewall, () => !_loadFailed);

        Reload();
    }

    public ObservableCollection<AllowedAppRowViewModel> Rows { get; } = new();

    public AllowedAppRowViewModel? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                _removeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public System.Windows.Input.ICommand AddCommand => _addCommand;

    public System.Windows.Input.ICommand AddCommonCommand => _addCommonCommand;

    public System.Windows.Input.ICommand RemoveCommand => _removeCommand;

    public System.Windows.Input.ICommand ApplyCommand => _applyCommand;

    private void Reload()
    {
        try
        {
            _document = _store.Load();
            _loadFailed = false;
        }
        catch (InvalidDataException ex)
        {
            // Left loaded-but-broken on purpose. Replacing it with an empty list here and then
            // letting the user press Apply would delete every rule they had.
            _loadFailed = true;
            Status = $"Không đọc được danh sách: {ex.Message}";
            RefreshCommands();
            return;
        }

        Refresh();
    }

    private void Refresh()
    {
        Rows.Clear();
        foreach (AllowedApp app in _document.Apps.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            Rows.Add(new AllowedAppRowViewModel(app));
        }

        int missing = Rows.Count(r => r.FileMissing);
        Status = Rows.Count == 0
            ? "Chưa có ứng dụng nào được phép."
            : string.Create(
                CultureInfo.CurrentCulture,
                $"{Rows.Count} ứng dụng được phép{(missing > 0 ? $" · {missing} file không còn tồn tại" : string.Empty)}.");

        RefreshCommands();
    }

    private void RefreshCommands()
    {
        _addCommand.RaiseCanExecuteChanged();
        _addCommonCommand.RaiseCanExecuteChanged();
        _removeCommand.RaiseCanExecuteChanged();
        _applyCommand.RaiseCanExecuteChanged();
    }

    private void Add()
    {
        string[]? paths = _pickFiles();
        if (paths is null || paths.Length == 0)
        {
            return;
        }

        var added = new List<string>();

        foreach (string path in paths)
        {
            if (_document.Apps.Any(a =>
                    string.Equals(a.ExecutablePath, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            string fileName = System.IO.Path.GetFileName(path);

            if (Interpreters.Contains(fileName)
                && !_confirm(
                    "Cho phép công cụ này?",
                    $"{fileName} có thể chạy mã tuỳ ý. Cho phép nó ra internet nghĩa là cho phép "
                    + "mọi script chạy qua nó ra internet.\n\nVẫn thêm?"))
            {
                continue;
            }

            _document.Apps.Add(new AllowedApp
            {
                ExecutablePath = path,
                DisplayName = fileName,
                Sha256 = TryHash(path),
                AddedAt = DateTimeOffset.Now,
            });

            added.Add(fileName);
        }

        if (added.Count == 0)
        {
            return;
        }

        _store.Save(_document);
        Refresh();
        Status = $"Đã thêm {added.Count} ứng dụng. Bấm \"Áp dụng\" để ghi vào Windows Firewall.";
    }

    /// <summary>
    /// Adds the catalogued applications that are installed on this machine.
    /// </summary>
    /// <remarks>
    /// Only ones that exist. Adding a catalogue entry whose file is not there would create a rule
    /// Windows accepts and that then allows nothing - the worst kind of failure, because the
    /// allow-list would look correct.
    /// </remarks>
    private void AddCommonApps()
    {
        IReadOnlyList<DiscoveredApp> discovered;
        try
        {
            discovered = new KnownApps().Discover();
        }
        catch (InvalidDataException ex)
        {
            Status = $"Không đọc được danh mục ứng dụng: {ex.Message}";
            return;
        }

        var added = new List<string>();

        foreach (DiscoveredApp app in discovered)
        {
            if (_document.Apps.Any(a =>
                    string.Equals(a.ExecutablePath, app.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _document.Apps.Add(new AllowedApp
            {
                ExecutablePath = app.ExecutablePath,
                DisplayName = app.Name,
                Sha256 = TryHash(app.ExecutablePath),
                AddedAt = DateTimeOffset.Now,
            });

            added.Add(app.Name);
        }

        if (added.Count == 0)
        {
            Status = discovered.Count == 0
                ? "Không tìm thấy ứng dụng phổ biến nào được cài trên máy."
                : "Các ứng dụng tìm thấy đều đã có trong danh sách.";
            return;
        }

        _store.Save(_document);
        Refresh();
        Status = $"Đã thêm {added.Count}: {string.Join(", ", added)}. Bấm \"Áp dụng\" để ghi vào Windows Firewall.";
    }

    private void RemoveSelected()
    {
        if (_selected is null)
        {
            return;
        }

        _document.Apps.Remove(_selected.Source);
        _store.Save(_document);
        Refresh();
        Status = "Đã xoá khỏi danh sách. Bấm \"Áp dụng\" để cập nhật Windows Firewall.";
    }

    private void ApplyToFirewall()
    {
        if (!ElevationService.IsElevated)
        {
            Status = "Cần quyền Admin để ghi rule vào Windows Firewall.";
            return;
        }

        try
        {
            PolicyDiff diff = new PolicyApplier(_policyFactory()).Apply(_document);

            Status = diff.HasChanges
                ? $"Đã áp dụng: thêm {diff.ToAdd.Count}, xoá {diff.ToRemove.Count}, giữ nguyên {diff.Unchanged.Count}."
                : $"Windows Firewall đã khớp danh sách ({diff.Unchanged.Count} rule), không cần thay đổi.";
        }
        catch (UnauthorizedAccessException ex)
        {
            Status = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            // The applier refusing to touch a rule outside its own group lands here.
            Status = ex.Message;
        }
    }

    /// <summary>
    /// Recorded so a later build can tell that an allowed executable has been replaced.
    /// Null when the file cannot be read, which is not a reason to refuse the entry.
    /// </summary>
    private static string? TryHash(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
