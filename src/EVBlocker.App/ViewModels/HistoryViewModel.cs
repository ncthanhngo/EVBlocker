using System.Collections.ObjectModel;
using System.Globalization;
using EVBlocker.App.Mvvm;
using EVBlocker.App.Services;
using EVBlocker.Core.History;

namespace EVBlocker.App.ViewModels;

/// <summary>Display projection of one <see cref="AttemptSummary"/>.</summary>
public sealed class HistoryRowViewModel
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
    public string FullPath { get; }
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
    private readonly RelayCommand _refreshCommand;

    private LookbackOption _lookback;
    private string _status = string.Empty;
    private bool _isBusy;

    public HistoryViewModel(Func<IAttemptHistoryReader> readerFactory)
    {
        ArgumentNullException.ThrowIfNull(readerFactory);
        _readerFactory = readerFactory;

        _lookback = LookbackOptions[1];
        _refreshCommand = new RelayCommand(Refresh, () => !_isBusy);

        _status = ElevationService.IsElevated
            ? "Bấm Tải lại để đọc nhật ký."
            : "Cần quyền Admin để đọc Security log.";
    }

    public sealed record LookbackOption(string Label, TimeSpan Duration);

    public static IReadOnlyList<LookbackOption> LookbackOptions { get; } = new[]
    {
        new LookbackOption("1 giờ qua", TimeSpan.FromHours(1)),
        new LookbackOption("24 giờ qua", TimeSpan.FromHours(24)),
        new LookbackOption("7 ngày qua", TimeSpan.FromDays(7)),
    };

    public ObservableCollection<HistoryRowViewModel> Rows { get; } = new();

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

    private void Refresh()
    {
        _isBusy = true;
        _refreshCommand.RaiseCanExecuteChanged();

        try
        {
            Rows.Clear();

            IReadOnlyList<AttemptSummary> summaries =
                _readerFactory().ReadSummaries(_lookback.Duration, MaxEvents);

            foreach (AttemptSummary summary in summaries)
            {
                Rows.Add(new HistoryRowViewModel(summary));
            }

            Status = Rows.Count == 0
                // An empty result is ambiguous, so name the likely cause rather than leaving the
                // user staring at a blank grid.
                ? "Không có sự kiện nào. Nhật ký chỉ có dữ liệu sau khi bật audit policy."
                : string.Create(CultureInfo.CurrentCulture, $"{Rows.Count} ứng dụng trong {_lookback.Label.ToLowerInvariant()}.");
        }
        catch (UnauthorizedAccessException)
        {
            Status = "Không đọc được Security log: cần chạy với quyền Admin.";
        }
        catch (System.Diagnostics.Eventing.Reader.EventLogException ex)
        {
            Status = $"Không đọc được Security log: {ex.Message}";
        }
        finally
        {
            _isBusy = false;
            _refreshCommand.RaiseCanExecuteChanged();
        }
    }
}
