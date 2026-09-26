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

/// <summary>An executable picked out of the scan dialog.</summary>
public sealed record ScannedSelection(string Name, string ExecutablePath);

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
    private readonly Func<IReadOnlyList<ScannedSelection>?> _pickRunningApps;
    private readonly Func<string, string, bool> _confirm;

    private readonly RelayCommand _addCommand;
    private readonly RelayCommand _addCommonCommand;
    private readonly RelayCommand _scanCommand;
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
        Func<IReadOnlyList<ScannedSelection>?> pickRunningApps,
        Func<string, string, bool> confirm)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(policyFactory);
        ArgumentNullException.ThrowIfNull(pickFiles);
        ArgumentNullException.ThrowIfNull(pickRunningApps);
        ArgumentNullException.ThrowIfNull(confirm);

        _store = store;
        _policyFactory = policyFactory;
        _pickFiles = pickFiles;
        _pickRunningApps = pickRunningApps;
        _confirm = confirm;

        _addCommand = new RelayCommand(Add, () => !_loadFailed);
        _addCommonCommand = new RelayCommand(AddCommonApps, () => !_loadFailed);
        _scanCommand = new RelayCommand(ScanRunningApps, () => !_loadFailed);
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

    public System.Windows.Input.ICommand ScanCommand => _scanCommand;

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

        SeedResult seeded = SeedDefaults();
        Refresh();

        if (seeded.Count > 0)
        {
            Status = $"Đã thêm mặc định {seeded.Count}: {string.Join(", ", seeded.Names)}. "
                + "Bấm \"Áp dụng\" để ghi vào Windows Firewall.";

            if (seeded.ScriptHosts.Count > 0)
            {
                // Adding one of these by hand asks first. Seeding cannot ask - nobody is at the
                // keyboard yet - so it says so instead of letting the warning quietly not happen.
                Status += $" Trong đó {string.Join(", ", seeded.ScriptHosts)} chạy được mã tuỳ ý: "
                    + "cho phép chúng là cho phép mọi script chạy qua chúng.";
            }
        }
    }

    /// <summary>
    /// Puts the catalogued applications installed on this machine into the list.
    /// </summary>
    /// <remarks>
    /// Runs on every load rather than only on a fresh machine, so an application installed later
    /// becomes a default too without the user having to know a button exists. Removals are
    /// recorded in the document, which is what stops this from undoing them.
    /// </remarks>
    private SeedResult SeedDefaults()
    {
        IReadOnlyList<DiscoveredApp> missing;
        try
        {
            missing = DefaultAllowList.MissingFrom(_document, new KnownApps().Discover());
        }
        catch (InvalidDataException)
        {
            // An unreadable catalogue costs the defaults, not the list the user already has.
            return SeedResult.None;
        }

        if (missing.Count == 0)
        {
            return SeedResult.None;
        }

        foreach (DiscoveredApp app in missing)
        {
            AddEntry(app.ExecutablePath, app.Name);
        }

        _store.Save(_document);

        return new SeedResult(
            missing.Select(app => app.Name).ToList(),
            missing
                .Where(app => Interpreters.Contains(System.IO.Path.GetFileName(app.ExecutablePath)))
                .Select(app => app.Name)
                .ToList());
    }

    /// <summary>What a seed added, and which of those can run arbitrary code.</summary>
    private sealed record SeedResult(IReadOnlyList<string> Names, IReadOnlyList<string> ScriptHosts)
    {
        public static readonly SeedResult None =
            new(Array.Empty<string>(), Array.Empty<string>());

        public int Count => Names.Count;
    }

    /// <summary>Adds one executable, and clears any record of it having been removed before.</summary>
    /// <remarks>
    /// Adding something back is a decision that replaces the earlier one to take it out;
    /// leaving the removal recorded would make the entry vanish again at the next load.
    /// </remarks>
    private void AddEntry(string executablePath, string displayName)
    {
        _document.Apps.Add(new AllowedApp
        {
            ExecutablePath = executablePath,
            DisplayName = displayName,
            Sha256 = TryHash(executablePath),
            AddedAt = DateTimeOffset.Now,
        });

        _document.RemovedDefaults.RemoveAll(
            path => string.Equals(path, executablePath, StringComparison.OrdinalIgnoreCase));
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
        _scanCommand.RaiseCanExecuteChanged();
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

            AddEntry(path, fileName);

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
    /// <summary>
    /// Opens the scan dialog and adds whatever the user ticked.
    /// </summary>
    /// <remarks>
    /// No block rules are written for the ones left unticked. Default-deny already blocks
    /// everything without an allow rule, and an explicit block list would be strictly weaker:
    /// anything installed after the scan would not be on it, and would therefore be allowed.
    /// </remarks>
    private void ScanRunningApps()
    {
        IReadOnlyList<ScannedSelection>? picked = _pickRunningApps();
        if (picked is null || picked.Count == 0)
        {
            return;
        }

        var added = new List<string>();

        foreach (ScannedSelection app in picked)
        {
            if (_document.Apps.Any(a =>
                    string.Equals(a.ExecutablePath, app.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (Interpreters.Contains(app.Name)
                && !_confirm(
                    "Cho phép công cụ này?",
                    $"{app.Name} có thể chạy mã tuỳ ý. Cho phép nó ra internet nghĩa là cho phép "
                    + "mọi script chạy qua nó ra internet.\n\nVẫn thêm?"))
            {
                continue;
            }

            AddEntry(app.ExecutablePath, app.Name);

            added.Add(app.Name);
        }

        if (added.Count == 0)
        {
            Status = "Những ứng dụng đã chọn đều có sẵn trong danh sách.";
            return;
        }

        _store.Save(_document);
        Refresh();
        Status = $"Đã thêm {added.Count} ứng dụng từ kết quả quét. Bấm \"Áp dụng\" để ghi vào Windows Firewall.";
    }

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

            AddEntry(app.ExecutablePath, app.Name);

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

        string removed = _selected.Source.ExecutablePath;
        _document.Apps.Remove(_selected.Source);

        // Recorded so the catalogue does not seed it back at the next load. Without this a
        // default could be removed but never stay removed.
        if (!_document.RemovedDefaults.Any(
                path => string.Equals(path, removed, StringComparison.OrdinalIgnoreCase)))
        {
            _document.RemovedDefaults.Add(removed);
        }

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
