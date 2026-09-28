using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using EVBlocker.App.Mvvm;
using EVBlocker.App.Services;
using EVBlocker.Core.Ssh;

namespace EVBlocker.App.ViewModels;

/// <summary>
/// The SSH panel: a password gate, the list of machines, and one click to open a terminal into
/// any of them as administrator.
/// </summary>
/// <remarks>
/// Everything that touches WPF or the outside world - the clipboard, launching ssh, running the
/// elevated setup - arrives as a delegate, so the panel's logic can be exercised without a window
/// and holds no reference to a process or a dialog. The gate is a convenience lock, not security:
/// the machine list sits in a readable file, so the password only keeps a passer-by out.
/// </remarks>
public sealed class SshViewModel : ObservableObject
{
    private const string DefaultPassword = "3214";

    private static readonly TimeSpan DiscoveryWindow = TimeSpan.FromSeconds(2);

    private readonly UserSettings _settings;
    private readonly SshHostStore _store;
    private readonly Func<string> _ensurePublicKey;
    private readonly Action<SshHost> _connect;
    private readonly Func<string, ProvisionResult> _provision;
    private readonly Action<string> _copyToClipboard;
    private readonly Func<TimeSpan, Task<IReadOnlyList<DiscoveredMachine>>> _discover;

    private readonly RelayCommand _unlockCommand;
    private readonly RelayCommand _addCommand;
    private readonly RelayCommand _removeCommand;
    private readonly RelayCommand _copyKeyCommand;
    private readonly RelayCommand _prepareCommand;
    private readonly RelayCommand _rediscoverCommand;

    private SshHostList _list = new();
    private bool _unlocked;
    private string _passwordInput = string.Empty;
    private string _gateMessage = string.Empty;
    private SshHostRowViewModel? _selected;
    private string _publicKey = string.Empty;
    private string _adminPublicKeyInput = string.Empty;
    private string _keyMessage = string.Empty;
    private string _status = string.Empty;
    private bool _busy;

    public SshViewModel(
        UserSettings settings,
        SshHostStore store,
        Func<string> ensurePublicKey,
        Action<SshHost> connect,
        Func<string, ProvisionResult> provision,
        Action<string> copyToClipboard,
        Func<TimeSpan, Task<IReadOnlyList<DiscoveredMachine>>> discover)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(ensurePublicKey);
        ArgumentNullException.ThrowIfNull(connect);
        ArgumentNullException.ThrowIfNull(provision);
        ArgumentNullException.ThrowIfNull(copyToClipboard);
        ArgumentNullException.ThrowIfNull(discover);

        _settings = settings;
        _store = store;
        _ensurePublicKey = ensurePublicKey;
        _connect = connect;
        _provision = provision;
        _copyToClipboard = copyToClipboard;
        _discover = discover;

        _unlockCommand = new RelayCommand(Unlock);
        _addCommand = new RelayCommand(Add);
        _removeCommand = new RelayCommand(RemoveSelected, () => _selected is not null);
        _copyKeyCommand = new RelayCommand(() => _copyToClipboard(_publicKey), () => _publicKey.Length > 0);
        _prepareCommand = new RelayCommand(() => _ = PrepareThisMachineAsync(), () => !_busy && _adminPublicKeyInput.Trim().Length > 0);
        _rediscoverCommand = new RelayCommand(() => _ = DiscoverAndMergeAsync(), () => !_busy);
    }

    public ObservableCollection<SshHostRowViewModel> Hosts { get; } = new();

    public bool Unlocked
    {
        get => _unlocked;
        private set
        {
            if (SetProperty(ref _unlocked, value))
            {
                OnPropertyChanged(nameof(Locked));
            }
        }
    }

    /// <summary>The inverse, so the gate and the panel can each bind a visibility without a converter trick.</summary>
    public bool Locked => !_unlocked;

    public string PasswordInput
    {
        get => _passwordInput;
        set => SetProperty(ref _passwordInput, value);
    }

    public string GateMessage
    {
        get => _gateMessage;
        private set => SetProperty(ref _gateMessage, value);
    }

    public SshHostRowViewModel? Selected
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

    /// <summary>This machine's public key, shown so it can be copied onto the machines.</summary>
    public string PublicKey
    {
        get => _publicKey;
        private set
        {
            if (SetProperty(ref _publicKey, value))
            {
                _copyKeyCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// The key to authorise when preparing a machine. Pre-filled with this machine's own key, so
    /// on the admin's machine the button just works; on a target, the admin pastes their key here.
    /// </summary>
    public string AdminPublicKeyInput
    {
        get => _adminPublicKeyInput;
        set
        {
            if (SetProperty(ref _adminPublicKeyInput, value))
            {
                _prepareCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string KeyMessage
    {
        get => _keyMessage;
        private set => SetProperty(ref _keyMessage, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool IsElevated => ElevationService.IsElevated;

    public System.Windows.Input.ICommand UnlockCommand => _unlockCommand;
    public System.Windows.Input.ICommand AddCommand => _addCommand;
    public System.Windows.Input.ICommand RemoveCommand => _removeCommand;
    public System.Windows.Input.ICommand CopyKeyCommand => _copyKeyCommand;
    public System.Windows.Input.ICommand PrepareCommand => _prepareCommand;
    public System.Windows.Input.ICommand RediscoverCommand => _rediscoverCommand;

    private void Unlock()
    {
        if (!PasswordMatches(_passwordInput))
        {
            GateMessage = "Mật khẩu chưa đúng.";
            return;
        }

        PasswordInput = string.Empty;
        GateMessage = string.Empty;
        Unlocked = true;

        LoadHosts();
        LoadKey();
        _ = DiscoverAndMergeAsync();
    }

    /// <summary>
    /// Asks the network which machines run EVBlocker and folds any new ones into the list.
    /// </summary>
    /// <remarks>
    /// Additive only. A machine already listed keeps its row and whatever name the admin gave it;
    /// discovery never renames or removes, so a found host does not overwrite a deliberate label,
    /// and a machine that is off today does not vanish from the list.
    /// </remarks>
    private async Task DiscoverAndMergeAsync()
    {
        _busy = true;
        _rediscoverCommand.RaiseCanExecuteChanged();
        Status = "Đang tìm máy trong mạng…";

        try
        {
            IReadOnlyList<DiscoveredMachine> machines = await _discover(DiscoveryWindow).ConfigureAwait(true);

            int added = 0;
            foreach (DiscoveredMachine machine in machines)
            {
                if (_list.Hosts.Any(h => string.Equals(h.Address.Trim(), machine.Address, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var host = new SshHost
                {
                    DisplayName = machine.HostName,
                    Address = machine.Address,
                    Username = Environment.UserName,
                };
                _list.Hosts.Add(host);
                Hosts.Add(NewRow(host));
                added++;
            }

            if (added > 0)
            {
                Persist();
            }

            Status = added > 0
                ? $"Tìm thấy thêm {added} máy. Tổng {Hosts.Count} máy."
                : $"{Hosts.Count} máy. Không thấy máy mới trong mạng.";
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or InvalidOperationException)
        {
            Status = $"Không tìm được máy trong mạng: {ex.Message}";
        }
        finally
        {
            _busy = false;
            _rediscoverCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>The stored hash, or the built-in default when none has been set.</summary>
    private bool PasswordMatches(string input)
    {
        string target = string.IsNullOrEmpty(_settings.SshGateHash) ? Hash(DefaultPassword) : _settings.SshGateHash;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Hash(input)), Encoding.ASCII.GetBytes(target));
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private void LoadHosts()
    {
        _list = _store.Load();
        Hosts.Clear();
        foreach (SshHost host in _list.Hosts)
        {
            Hosts.Add(NewRow(host));
        }

        Status = Hosts.Count == 0
            ? "Chưa có máy nào. Bấm \"Thêm máy\" rồi điền tên và địa chỉ IP."
            : $"{Hosts.Count} máy.";
    }

    private SshHostRowViewModel NewRow(SshHost host) =>
        new(host, _connect, _ => Persist());

    private void LoadKey()
    {
        try
        {
            PublicKey = _ensurePublicKey();
            if (_adminPublicKeyInput.Length == 0)
            {
                AdminPublicKeyInput = PublicKey;
            }

            KeyMessage = "Đây là khoá công khai của máy này. Máy đích phải có khoá này thì mới cho bạn vào.";
        }
        catch (InvalidOperationException ex)
        {
            KeyMessage = ex.Message;
        }
    }

    private void Add()
    {
        var host = new SshHost { DisplayName = "Máy mới", Username = Environment.UserName };
        _list.Hosts.Add(host);

        SshHostRowViewModel row = NewRow(host);
        Hosts.Add(row);
        Selected = row;
        Persist();
    }

    private void RemoveSelected()
    {
        if (_selected is null)
        {
            return;
        }

        _list.Hosts.Remove(_selected.Model);
        Hosts.Remove(_selected);
        Selected = null;
        Persist();
    }

    private void Persist()
    {
        try
        {
            _store.Save(_list);
            Status = $"{Hosts.Count} máy.";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            Status = $"Không lưu được danh sách: {ex.Message}";
        }
    }

    private async Task PrepareThisMachineAsync()
    {
        if (!ElevationService.IsElevated)
        {
            Status = "Cần chạy với quyền quản trị để chuẩn bị máy nhận SSH.";
            return;
        }

        _busy = true;
        _prepareCommand.RaiseCanExecuteChanged();
        Status = "Đang bật SSH trên máy này… có thể mất vài phút.";

        string key = _adminPublicKeyInput.Trim();

        try
        {
            ProvisionResult result = await Task.Run(() => _provision(key)).ConfigureAwait(true);
            Status = result.Detail;
        }
        finally
        {
            _busy = false;
            _prepareCommand.RaiseCanExecuteChanged();
        }
    }
}
