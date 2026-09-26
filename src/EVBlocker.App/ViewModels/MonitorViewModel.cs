using EVBlocker.App.Mvvm;

namespace EVBlocker.App.ViewModels;

/// <summary>
/// The two observation views behind one rail entry.
/// </summary>
/// <remarks>
/// They answer the same question in two tenses - what software is reaching the internet, and
/// what was stopped from reaching it - so they earn one place in the rail rather than two. The
/// rail's weight belongs to the decision the application exists for, which is the allow-list.
///
/// A switch rather than a merged table: the live view lists sockets with ports and states, the
/// history lists applications with counts. Forcing both into one grid would leave most columns
/// empty most of the time.
/// </remarks>
public sealed class MonitorViewModel : ObservableObject
{
    private bool _showHistory;

    public MonitorViewModel(ActiveConnectionsViewModel active, HistoryViewModel history)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(history);

        Active = active;
        History = history;
    }

    public ActiveConnectionsViewModel Active { get; }

    public HistoryViewModel History { get; }

    /// <summary>
    /// Which of the two is showing. Starts on the live view, which needs no setting up.
    /// </summary>
    /// <remarks>
    /// Both directions are writable so the two buttons can bind two-way and behave as a radio
    /// pair. Doing it through Click instead looked identical under the mouse and was wrong: a
    /// toggle driven through the automation interface calls OnToggle, which never raises Click,
    /// so the button changed colour while the view behind it did not move.
    /// </remarks>
    public bool ShowHistory
    {
        get => _showHistory;
        set => Select(history: value, selecting: value);
    }

    public bool ShowLive
    {
        get => !_showHistory;
        set => Select(history: !value, selecting: value);
    }

    /// <summary>
    /// Applies a write only when it selects; a clearing write is answered by re-announcing state.
    /// </summary>
    /// <remarks>
    /// The button being switched off writes false a moment before the other writes true. Taking
    /// that literally would flip to the other view, and clicking the active button would swap
    /// away from it. Re-raising is what snaps the rejected button back to where it was.
    /// </remarks>
    private void Select(bool history, bool selecting)
    {
        if (selecting)
        {
            _showHistory = history;
        }

        OnPropertyChanged(nameof(ShowHistory));
        OnPropertyChanged(nameof(ShowLive));
    }
}
