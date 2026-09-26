using System.Globalization;
// WPF leaves System.IO out of its implicit usings because System.Windows.Shapes.Path would
// collide with System.IO.Path.
using System.IO;
using System.Windows.Threading;
using EVBlocker.App.Mvvm;
using EVBlocker.App.Services;
using EVBlocker.Core.Firewall;
using EVBlocker.Core.Policy;
using EVBlocker.Core.Startup;

namespace EVBlocker.App.ViewModels;

/// <summary>Drives the enforcement strip: current state, and the three things you can do to it.</summary>
public sealed class EnforcementViewModel : ObservableObject
{
    public sealed record RevertOption(string Label, TimeSpan Duration);

    /// <summary>
    /// How long the user gets to check the machine still works. Bounded by the dead-man switch
    /// itself; these are the choices worth offering.
    /// </summary>
    public static IReadOnlyList<RevertOption> RevertOptions { get; } = new[]
    {
        new RevertOption("5 phút", TimeSpan.FromMinutes(5)),
        new RevertOption("10 phút", TimeSpan.FromMinutes(10)),
        new RevertOption("30 phút", TimeSpan.FromMinutes(30)),
    };

    private readonly EnforcementController _controller;
    private readonly AllowListStore _store;
    private readonly StartupReconcileTask _startupTask;
    private readonly Func<string, string, bool> _confirm;
    private readonly DispatcherTimer _countdown;

    private readonly RelayCommand _enableCommand;
    private readonly RelayCommand _confirmCommand;
    private readonly RelayCommand _disableCommand;
    private readonly RelayCommand _toggleStartupCommand;

    private bool _startupInstalled;

    private EnforcementStatus? _status;
    private RevertOption _revertAfter;
    private string _message = string.Empty;

    /// <summary>
    /// When the pending revert fires, known only if this session armed it.
    /// </summary>
    /// <remarks>
    /// The state itself is read from the machine, which is what makes it trustworthy, but the
    /// machine does not hand back a countdown without parsing localised scheduler output. So a
    /// restarted app reports that a revert is pending and declines to invent a time for it.
    /// </remarks>
    private DateTimeOffset? _revertAt;

    public EnforcementViewModel(
        EnforcementController controller,
        AllowListStore store,
        StartupReconcileTask startupTask,
        Func<string, string, bool> confirm)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(startupTask);
        ArgumentNullException.ThrowIfNull(confirm);

        _controller = controller;
        _store = store;
        _startupTask = startupTask;
        _confirm = confirm;
        _revertAfter = RevertOptions[1];
        _toggleStartupCommand = new RelayCommand(ToggleStartupTask, () => ElevationService.IsElevated);

        _enableCommand = new RelayCommand(Enable, () => State == EnforcementState.Off && ElevationService.IsElevated);
        _confirmCommand = new RelayCommand(ConfirmEnforcement, () => State == EnforcementState.Armed && ElevationService.IsElevated);
        _disableCommand = new RelayCommand(Disable, () => State != EnforcementState.Off && ElevationService.IsElevated);

        _countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdown.Tick += (_, _) => OnPropertyChanged(nameof(RevertCountdown));

        Refresh();
    }

    public System.Windows.Input.ICommand EnableCommand => _enableCommand;

    public System.Windows.Input.ICommand ConfirmCommand => _confirmCommand;

    public System.Windows.Input.ICommand DisableCommand => _disableCommand;

    public System.Windows.Input.ICommand ToggleStartupCommand => _toggleStartupCommand;

    public EnforcementState State => _status?.State ?? EnforcementState.Off;

    /// <summary>
    /// Whether the boot-time reconcile task is registered.
    /// </summary>
    /// <remarks>
    /// Worth surfacing because its absence is invisible otherwise: blocking keeps working without
    /// it, right up until something removes the rules and nothing puts them back.
    /// </remarks>
    public bool StartupTaskInstalled => _startupInstalled;

    public string StartupTaskText => _startupInstalled
        ? "Tự kiểm tra lại khi mở máy: BẬT"
        : "Tự kiểm tra lại khi mở máy: TẮT — nếu danh sách bị xoá, ứng dụng sẽ không tự đặt lại";

    public string StartupToggleLabel => _startupInstalled ? "Tắt tự kiểm tra" : "Bật tự kiểm tra";

    public RevertOption RevertAfter
    {
        get => _revertAfter;
        set => SetProperty(ref _revertAfter, value);
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public string StateText => State switch
    {
        EnforcementState.Off => "CHƯA CHẶN — mọi phần mềm đều ra được internet",
        EnforcementState.Armed => "ĐANG CHỜ XÁC NHẬN",
        EnforcementState.On => "ĐANG CHẶN — chỉ phần mềm trong danh sách cho phép mới ra được internet",
        _ => "Không xác định",
    };

    public bool ShowArmedControls => State == EnforcementState.Armed;

    public bool ShowEnableControls => State == EnforcementState.Off;

    /// <summary>Per-profile detail, shown because a partial state is otherwise invisible.</summary>
    public string ProfileDetail => _status is null
        ? string.Empty
        : string.Join(
            " · ",
            _status.DefaultOutbound.Select(p => $"{p.Key}: {(p.Value == FirewallAction.Block ? "chặn" : "cho phép")}"));

    public string RevertCountdown
    {
        get
        {
            if (State != EnforcementState.Armed)
            {
                return string.Empty;
            }

            if (_revertAt is null)
            {
                // Armed by a previous run of the app, so the deadline is not known here.
                return "Máy sẽ tự bỏ chặn, nhưng không rõ lúc nào — hẹn giờ do lần mở trước đặt.";
            }

            TimeSpan left = _revertAt.Value - DateTimeOffset.Now;

            return left <= TimeSpan.Zero
                ? "Đã tới giờ — đang bỏ chặn."
                : string.Create(
                    CultureInfo.CurrentCulture,
                    $"Tự bỏ chặn sau {left.Minutes:00}:{left.Seconds:00} nếu bạn không bấm Giữ nguyên.");
        }
    }

    public void Refresh()
    {
        try
        {
            _status = _controller.GetStatus();
            _startupInstalled = _startupTask.IsInstalled();
        }
        catch (UnauthorizedAccessException ex)
        {
            Message = ex.Message;
        }

        if (State == EnforcementState.Armed)
        {
            _countdown.Start();
        }
        else
        {
            _countdown.Stop();
            _revertAt = null;
        }

        RaiseAll();
    }

    public void Stop() => _countdown.Stop();

    private void Enable()
    {
        AllowListDocument allowList;
        try
        {
            allowList = _store.Load();
        }
        catch (InvalidDataException ex)
        {
            // Blocking with an unreadable allow-list would apply the baseline and nothing else,
            // cutting off every application the user had approved.
            Message = $"Không đọc được danh sách cho phép, chưa thể bắt đầu chặn: {ex.Message}";
            return;
        }

        if (!_confirm(
                "Bắt đầu chặn internet?",
                $"Mọi phần mềm ngoài danh sách cho phép ({allowList.Apps.Count} mục) sẽ không ra được internet.\n\n"
                + $"Cài đặt tường lửa hiện tại được sao lưu trước. Nếu bạn không bấm \"Giữ nguyên\" "
                + $"trong {_revertAfter.Label}, máy tự bỏ chặn — kể cả khi bạn tắt ứng dụng hoặc khởi động lại máy.\n\n"
                + "Tiếp tục?"))
        {
            return;
        }

        Run(() =>
        {
            _status = _controller.Enable(allowList, _revertAfter.Duration);
            _revertAt = DateTimeOffset.Now + _revertAfter.Duration;
            Message = $"Đã bắt đầu chặn. Thử xem mạng còn dùng được không, rồi bấm \"Giữ nguyên\".";
        });
    }

    private void ToggleStartupTask() => Run(() =>
    {
        if (_startupInstalled)
        {
            _startupTask.Uninstall();
            Message = "Đã tắt tự kiểm tra khi mở máy.";
            return;
        }

        // Environment.ProcessPath is the running executable, so the task points at whatever copy
        // the user actually launched rather than at a guessed install location.
        string? executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
        {
            Message = "Không xác định được đường dẫn của chính ứng dụng, chưa đăng ký được.";
            return;
        }

        _startupTask.Install(executable);
        Message = "Đã bật. Mỗi lần mở máy, danh sách sẽ được kiểm tra và đặt lại nếu bị thay đổi.";
    });

    private void ConfirmEnforcement() => Run(() =>
    {
        _status = _controller.Confirm();
        _revertAt = null;
        Message = "Đã giữ nguyên. Máy sẽ không tự bỏ chặn nữa.";
    });

    private void Disable() => Run(() =>
    {
        _status = _controller.Disable();
        _revertAt = null;
        Message = "Đã ngừng chặn.";
    });

    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (UnauthorizedAccessException ex)
        {
            Message = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            Message = ex.Message;
        }
        catch (InvalidDataException ex)
        {
            Message = ex.Message;
        }
        finally
        {
            Refresh();
        }
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(ProfileDetail));
        OnPropertyChanged(nameof(RevertCountdown));
        OnPropertyChanged(nameof(ShowArmedControls));
        OnPropertyChanged(nameof(ShowEnableControls));
        OnPropertyChanged(nameof(StartupTaskInstalled));
        OnPropertyChanged(nameof(StartupTaskText));
        OnPropertyChanged(nameof(StartupToggleLabel));

        _enableCommand.RaiseCanExecuteChanged();
        _confirmCommand.RaiseCanExecuteChanged();
        _disableCommand.RaiseCanExecuteChanged();
        _toggleStartupCommand.RaiseCanExecuteChanged();
    }
}
