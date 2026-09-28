using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using EVBlocker.App.Mvvm;
using EVBlocker.App.Services;
using EVBlocker.Core.Audit;
using EVBlocker.Core.History;

namespace EVBlocker.App.ViewModels;

/// <summary>Display projection of one <see cref="AttemptSummary"/>.</summary>
public sealed class HistoryRowViewModel : IAppRow
{
    public HistoryRowViewModel(AttemptSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        FullPath = summary.ExecutablePath;
        // Fully qualified: WPF drops System.IO from implicit usings, because
        // System.Windows.Shapes.Path would collide with System.IO.Path.
        AppName = System.IO.Path.GetFileName(summary.ExecutablePath) is { Length: > 0 } name
            ? name
            : summary.ExecutablePath;
        SoftwareDescription software = ExecutableDescriptions.Get(summary.ExecutablePath);
        Software = software.Product;
        Publisher = software.Publisher;
        BlockedCount = summary.BlockedCount;
        AllowedCount = summary.AllowedCount;
        LastSeen = summary.LastSeen == default
            ? "—"
            : summary.LastSeen.ToLocalTime().ToString("dd/MM HH:mm:ss", CultureInfo.CurrentCulture);
        Destinations = summary.SampleDestinations.Count == 0
            ? "—"
            : string.Join(", ", summary.SampleDestinations);
    }

    public string AppName { get; }
    public string? FullPath { get; }
    public string Software { get; }
    public string Publisher { get; }
    public string SoftwareTooltip => $"{Software}\nNhà phát hành: {Publisher}";
    public int BlockedCount { get; }
    public int AllowedCount { get; }
    public string LastSeen { get; }
    public string Destinations { get; }
}

/// <summary>
/// Reads the WFP audit log on demand.
/// </summary>
/// <remarks>
/// Not polled, unlike the live view: a log read walks thousands of events, so it runs when the
/// user asks rather than on a timer.
/// </remarks>
public sealed class HistoryViewModel : ObservableObject
{
    /// <summary>Bounds the work per read; the newest events are examined first.</summary>
    private const int MaxEvents = 20_000;

    private readonly Func<IAttemptHistoryReader> _readerFactory;
    private readonly Func<IAuditPolicy> _auditFactory;
    private readonly RelayCommand _refreshCommand;
    private readonly RelayCommand _enableLoggingCommand;

    private LookbackOption _lookback;
    private string _status = string.Empty;
    private bool _isBusy;
    private bool _loggingOn;
    private string _setupMessage = string.Empty;

    public HistoryViewModel(
        Func<IAttemptHistoryReader> readerFactory,
        Func<IAuditPolicy> auditFactory,
        AppAccessActions actions)
    {
        ArgumentNullException.ThrowIfNull(readerFactory);
        ArgumentNullException.ThrowIfNull(auditFactory);
        ArgumentNullException.ThrowIfNull(actions);
        Actions = actions;
        _readerFactory = readerFactory;
        _auditFactory = auditFactory;

        _lookback = LookbackOptions[1];
        _refreshCommand = new RelayCommand(() => _ = RefreshAsync(), () => !_isBusy);
        _enableLoggingCommand = new RelayCommand(EnableLogging, () => ElevationService.IsElevated && !_loggingOn);

        _status = ElevationService.IsElevated
            ? "Bấm Tải lại để xem."
            : "Cần quyền quản trị để đọc nhật ký của Windows.";

        ReadLoggingState();
    }

    public sealed record LookbackOption(string Label, TimeSpan Duration);

    public static IReadOnlyList<LookbackOption> LookbackOptions { get; } = new[]
    {
        new LookbackOption("1 giờ qua", TimeSpan.FromHours(1)),
        new LookbackOption("24 giờ qua", TimeSpan.FromHours(24)),
        new LookbackOption("7 ngày qua", TimeSpan.FromDays(7)),
    };

    public ObservableCollection<HistoryRowViewModel> Rows { get; } = new();

    /// <summary>Allow or revoke internet access for the selected row's executable.</summary>
    public AppAccessActions Actions { get; }

    public LookbackOption Lookback
    {
        get => _lookback;
        set => SetProperty(ref _lookback, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public System.Windows.Input.ICommand RefreshCommand => _refreshCommand;

    public System.Windows.Input.ICommand EnableLoggingCommand => _enableLoggingCommand;

    /// <summary>
    /// Why this page has nothing to show, and what to do about it. Empty once logging is on.
    /// </summary>
    /// <remarks>
    /// Without this the page is a dead end: Windows records nothing until the Filtering Platform
    /// Connection subcategory is switched on, so the grid stays empty however long the machine
    /// runs, and the only way to fix it was a command copied out of the documentation. The
    /// application already knew how to set it - the capability was simply never on screen.
    /// </remarks>
    public string SetupMessage
    {
        get => _setupMessage;
        private set => SetProperty(ref _setupMessage, value);
    }

    public bool ShowSetup => _setupMessage.Length > 0;

    /// <summary>Reads whether Windows is recording connection attempts at all.</summary>
    /// <remarks>
    /// The read itself needs administrator rights, so without them the answer is unknown rather
    /// than "off" - and the message says which of the two it is instead of guessing.
    /// </remarks>
    private void ReadLoggingState()
    {
        if (!ElevationService.IsElevated)
        {
            _loggingOn = false;
            SetupMessage = "Windows chỉ ghi lại các lần phần mềm cố ra internet sau khi bạn bật "
                + "ghi nhật ký. Cần quyền quản trị để kiểm tra và bật.";
            OnPropertyChanged(nameof(ShowSetup));
            return;
        }

        try
        {
            _loggingOn = _auditFactory().GetConnectionAudit().HasFlag(AuditSetting.Failure);
            SetupMessage = _loggingOn
                ? string.Empty
                : "Windows chưa ghi lại các lần phần mềm bị chặn. Bật ghi nhật ký để trang này "
                  + "có dữ liệu — chỉ ghi từ lúc bật trở đi.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            _loggingOn = false;
            SetupMessage = $"Không kiểm tra được trạng thái ghi nhật ký: {ex.Message}";
        }

        OnPropertyChanged(nameof(ShowSetup));
        _enableLoggingCommand.RaiseCanExecuteChanged();
    }

    private void EnableLogging()
    {
        try
        {
            IAuditPolicy policy = _auditFactory();

            // Failures only. Recording successes as well means an event for every connection the
            // machine makes, which fills the Security log in hours and buries what matters here.
            policy.SetConnectionAudit(policy.GetConnectionAudit() | AuditSetting.Failure);

            ReadLoggingState();
            Status = "Đã bật ghi nhật ký. Windows chỉ ghi từ bây giờ — dùng máy một lúc rồi quay "
                + "lại bấm Tải lại.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            Status = $"Không bật được ghi nhật ký: {ex.Message}";
        }
    }

    /// <summary>
    /// Reads the log on a background thread.
    /// </summary>
    /// <remarks>
    /// The read walks up to twenty thousand event records and parses XML for each one. Doing that
    /// on the dispatcher would lock the window for as long as it takes, with no way to tell
    /// whether the app had hung. Only the assignment of the rows comes back to the UI thread.
    ///
    /// Every exception is turned into a status line rather than escaping, because this runs as a
    /// fire-and-forget task from a command: an exception thrown here would surface as an
    /// unobserved task exception, long after the button was pressed and far from it.
    /// </remarks>
    private async Task RefreshAsync()
    {
        _isBusy = true;
        _refreshCommand.RaiseCanExecuteChanged();
        Status = "Đang đọc nhật ký…";

        // Captured before leaving the UI thread; the user can change the selection while the
        // read is running, and the result must describe the window that was actually read.
        LookbackOption lookback = _lookback;

        try
        {
            IReadOnlyList<AttemptSummary> summaries = await Task
                .Run(() => _readerFactory().ReadSummaries(lookback.Duration, MaxEvents))
                .ConfigureAwait(true);

            Rows.Clear();
            foreach (AttemptSummary summary in summaries)
            {
                Rows.Add(new HistoryRowViewModel(summary));
            }

            Status = Rows.Count == 0
                // An empty result is ambiguous, so name the likely cause rather than leaving the
                // user staring at a blank grid.
                ? "Chưa có gì. Windows chỉ ghi lại sau khi bạn bật ghi nhật ký — xem hướng dẫn sử dụng."
                : string.Create(CultureInfo.CurrentCulture, $"{Rows.Count} ứng dụng trong {lookback.Label.ToLowerInvariant()}.");
        }
        catch (UnauthorizedAccessException)
        {
            Status = "Không đọc được nhật ký: cần chạy với quyền quản trị.";
        }
        catch (System.Diagnostics.Eventing.Reader.EventLogException ex)
        {
            Status = $"Không đọc được nhật ký của Windows: {ex.Message}";
        }
        catch (Exception ex)
        {
            Status = $"Không đọc được nhật ký: {ex.Message}";
        }
        finally
        {
            _isBusy = false;
            _refreshCommand.RaiseCanExecuteChanged();
        }
    }
}
