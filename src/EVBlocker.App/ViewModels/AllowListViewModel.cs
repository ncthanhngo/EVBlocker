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
    private readonly Func<IReadOnlyList<ScannedSelection>?> _pickInstalledApps;
    private readonly Func<string, string, bool> _confirm;

    private readonly RelayCommand _addCommand;
    private readonly RelayCommand _addCommonCommand;
    private readonly RelayCommand _scanCommand;
    private readonly RelayCommand _installedCommand;
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
        Func<IReadOnlyList<ScannedSelection>?> pickInstalledApps,
        Func<string, string, bool> confirm)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(policyFactory);
        ArgumentNullException.ThrowIfNull(pickFiles);
        ArgumentNullException.ThrowIfNull(pickRunningApps);
        ArgumentNullException.ThrowIfNull(pickInstalledApps);
        ArgumentNullException.ThrowIfNull(confirm);

        _store = store;
        _policyFactory = policyFactory;
        _pickFiles = pickFiles;
        _pickRunningApps = pickRunningApps;
        _pickInstalledApps = pickInstalledApps;
        _confirm = confirm;

        _addCommand = new RelayCommand(Add, () => !_loadFailed);
        _addCommonCommand = new RelayCommand(AddCommonApps, () => !_loadFailed);
        _scanCommand = new RelayCommand(ScanRunningApps, () => !_loadFailed);
        _installedCommand = new RelayCommand(PickInstalled, () => !_loadFailed);
        _removeCommand = new RelayCommand(RemoveSelected, () => _selected is not null && !_loadFailed);
        _applyCommand = new RelayCommand(Apply, () => !_loadFailed);

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

    public System.Windows.Input.ICommand InstalledCommand => _installedCommand;

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
            Status = $"Không đọc được danh sách cho phép: {ex.Message}";
            RefreshCommands();
            return;
        }

        SeedResult seeded = SeedDefaults();
        Refresh();

        if (seeded.Count > 0)
        {
            Status = $"Đã tự cho phép {seeded.Count} phần mềm quen thuộc: {string.Join(", ", seeded.Names)}. "
                + "Bấm \"Lưu vào tường lửa\" để áp dụng.";

            if (seeded.ScriptHosts.Count > 0)
            {
                // Adding one of these by hand asks first. Seeding cannot ask - nobody is at the
                // keyboard yet - so it says so instead of letting the warning quietly not happen.
                Status += $" Trong đó {string.Join(", ", seeded.ScriptHosts)} chạy được lệnh bất kỳ: "
                    + "cho phép chúng nghĩa là mọi lệnh chạy qua chúng cũng ra được internet.";
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
            ? "Chưa cho phép phần mềm nào."
            : string.Create(
                CultureInfo.CurrentCulture,
                $"Đang cho phép {Rows.Count} phần mềm{(missing > 0 ? $" · {missing} file không còn trên máy" : string.Empty)}.");

        RefreshCommands();
    }

    private void RefreshCommands()
    {
        _addCommand.RaiseCanExecuteChanged();
        _addCommonCommand.RaiseCanExecuteChanged();
        _scanCommand.RaiseCanExecuteChanged();
        _installedCommand.RaiseCanExecuteChanged();
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
                    $"{fileName} chạy được lệnh bất kỳ. Cho phép nó ra internet nghĩa là mọi lệnh "
                    + "chạy qua nó cũng ra được internet.\n\nVẫn cho phép?"))
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
        Status = $"Đã cho phép thêm {added.Count} phần mềm. Bấm \"Lưu vào tường lửa\" để áp dụng.";
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
    /// <summary>Opens the installed-programs picker and adds whatever was ticked.</summary>
    private void PickInstalled()
    {
        IReadOnlyList<ScannedSelection>? picked = _pickInstalledApps();
        if (picked is null || picked.Count == 0)
        {
            return;
        }

        int added = AddApps(picked);

        Status = added == 0
            ? "Những phần mềm đã chọn đều được cho phép rồi."
            : $"Đã cho phép thêm {added} file. Bấm \"Lưu vào tường lửa\" để áp dụng.";
    }

    private void ScanRunningApps()
    {
        IReadOnlyList<ScannedSelection>? picked = _pickRunningApps();
        if (picked is null || picked.Count == 0)
        {
            return;
        }

        int added = AddApps(picked);

        Status = added == 0
            ? "Những phần mềm đã chọn đều được cho phép rồi."
            : $"Đã cho phép thêm {added} phần mềm. Bấm \"Lưu vào tường lửa\" để áp dụng.";
    }

    /// <summary>
    /// Adds a set of executables, skipping ones already present and asking before any that can
    /// run arbitrary code. Returns how many were added.
    /// </summary>
    /// <remarks>
    /// Public because more than one view ends here: the running-app scan and the installed-program
    /// list make the same decision, and a second copy of these checks is a second place for the
    /// confirmation prompt to go missing.
    /// </remarks>
    public int AddApps(IReadOnlyList<ScannedSelection> apps)
    {
        ArgumentNullException.ThrowIfNull(apps);

        if (_loadFailed)
        {
            return 0;
        }

        int added = 0;

        foreach (ScannedSelection app in apps)
        {
            if (_document.Apps.Any(a =>
                    string.Equals(a.ExecutablePath, app.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            // Keyed on the file name rather than the display name: an installed-program row is
            // called "Node.js", and only the file behind it says node.exe.
            string fileName = System.IO.Path.GetFileName(app.ExecutablePath);

            if (Interpreters.Contains(fileName)
                && !_confirm(
                    "Cho phép công cụ này?",
                    $"{fileName} chạy được lệnh bất kỳ. Cho phép nó ra internet nghĩa là mọi lệnh "
                    + "chạy qua nó cũng ra được internet.\n\nVẫn cho phép?"))
            {
                continue;
            }

            AddEntry(app.ExecutablePath, app.Name);
            added++;
        }

        if (added > 0)
        {
            _store.Save(_document);
            Refresh();
        }

        return added;
    }

    /// <summary>The executables currently allowed, for a view that shows what is already decided.</summary>
    public IReadOnlySet<string> AllowedPaths() =>
        _document.Apps.Select(app => app.ExecutablePath).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private void AddCommonApps()
    {
        IReadOnlyList<DiscoveredApp> discovered;
        try
        {
            discovered = new KnownApps().Discover();
        }
        catch (InvalidDataException ex)
        {
            Status = $"Không đọc được danh mục phần mềm quen thuộc: {ex.Message}";
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
                ? "Không tìm thấy phần mềm quen thuộc nào trên máy này."
                : "Những phần mềm tìm thấy đều đã được cho phép rồi.";
            return;
        }

        _store.Save(_document);
        Refresh();
        Status = $"Đã cho phép {added.Count}: {string.Join(", ", added)}. Bấm \"Lưu vào tường lửa\" để áp dụng.";
    }

    private void RemoveSelected()
    {
        if (_selected is null)
        {
            return;
        }

        RemoveEntry(_selected.Source);
        _store.Save(_document);
        Refresh();
        Status = "Đã bỏ khỏi danh sách. Bấm \"Lưu vào tường lửa\" để áp dụng.";
    }

    private void RemoveEntry(AllowedApp app)
    {
        _document.Apps.Remove(app);

        // Recorded so the catalogue does not seed it back at the next load. Without this a
        // default could be removed but never stay removed.
        if (!_document.RemovedDefaults.Any(
                path => string.Equals(path, app.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
        {
            _document.RemovedDefaults.Add(app.ExecutablePath);
        }
    }

    /// <summary>Whether <paramref name="executablePath"/> is on the list.</summary>
    public bool IsAllowed(string executablePath) =>
        _document.Apps.Any(a => string.Equals(a.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Allows one executable and writes the list to the firewall at once. Returns what happened.
    /// </summary>
    /// <remarks>
    /// Reached from a right-click on the monitoring page. Unlike adding on this page, it does not
    /// wait for "Lưu vào tường lửa": the person is looking at a program being refused right now,
    /// and a second step on another page is one they would not know to take.
    /// </remarks>
    public string AllowNow(string executablePath, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        if (_loadFailed)
        {
            return Status;
        }

        if (!IsAllowed(executablePath)
            && AddApps(new[] { new ScannedSelection(displayName, executablePath) }) == 0)
        {
            return $"Chưa cho phép {displayName}.";
        }

        return ApplyToFirewall()
            ? $"Đã cho phép {displayName} kết nối internet."
            : $"Đã thêm {displayName} vào danh sách cho phép, nhưng chưa ghi được vào tường lửa: {Status}";
    }

    /// <summary>
    /// Takes one executable off the list and writes the list to the firewall at once.
    /// </summary>
    /// <remarks>
    /// There is no block rule to add: once off the list, default-deny refuses it. That also means
    /// it does nothing while blocking is switched off, which the caller says.
    /// </remarks>
    /// <returns>Whether the firewall now refuses it, and what happened in words.</returns>
    public (bool Applied, string Message) RevokeNow(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        if (_loadFailed)
        {
            return (false, Status);
        }

        string name = System.IO.Path.GetFileName(executablePath);
        List<AllowedApp> entries = _document.Apps
            .Where(a => string.Equals(a.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (entries.Count == 0)
        {
            return (false, $"{name} không có trong danh sách cho phép.");
        }

        foreach (AllowedApp app in entries)
        {
            RemoveEntry(app);
        }

        _store.Save(_document);
        Refresh();

        return ApplyToFirewall()
            ? (true, $"Đã hủy kết nối internet của {name}.")
            : (false, $"Đã bỏ {name} khỏi danh sách cho phép, nhưng chưa ghi được vào tường lửa: {Status}");
    }

    private void Apply() => ApplyToFirewall();

    /// <summary>Writes the list into Windows Firewall. Returns false and sets Status on failure.</summary>
    private bool ApplyToFirewall()
    {
        if (!ElevationService.IsElevated)
        {
            Status = "Cần quyền quản trị để ghi vào tường lửa của Windows.";
            return false;
        }

        try
        {
            PolicyDiff diff = new PolicyApplier(_policyFactory()).Apply(_document);

            Status = diff.HasChanges
                ? $"Đã lưu: thêm {diff.ToAdd.Count}, bỏ {diff.ToRemove.Count}, giữ nguyên {diff.Unchanged.Count}."
                : $"Tường lửa đã khớp danh sách ({diff.Unchanged.Count} mục), không cần thay đổi gì.";
            return true;
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

        return false;
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
