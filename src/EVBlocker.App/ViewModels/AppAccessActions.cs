using EVBlocker.App.Mvvm;

namespace EVBlocker.App.ViewModels;

/// <summary>A monitoring row that names an executable.</summary>
public interface IAppRow
{
    string AppName { get; }

    string? FullPath { get; }
}

/// <summary>
/// The right-click decision on a monitoring row: let this software reach the internet, or take
/// that away.
/// </summary>
/// <remarks>
/// Shared by the live view and the history, which both list software and differ only in tense.
/// The decision itself belongs to the allow-list, so it arrives as delegates and this class only
/// tracks which row is selected and what the last action said.
/// </remarks>
public sealed class AppAccessActions : ObservableObject
{
    private readonly Func<string, string, string> _allow;
    private readonly Func<string, string> _revoke;
    private readonly Func<string, bool> _isAllowed;
    private readonly RelayCommand _allowCommand;
    private readonly RelayCommand _revokeCommand;

    private IAppRow? _selected;
    private string _message = string.Empty;

    /// <param name="allow">Allows (path, name) and returns what happened, in words.</param>
    /// <param name="revoke">Takes the path off the allow-list and returns what happened.</param>
    /// <param name="isAllowed">Whether the path is on the allow-list now.</param>
    public AppAccessActions(
        Func<string, string, string> allow,
        Func<string, string> revoke,
        Func<string, bool> isAllowed)
    {
        ArgumentNullException.ThrowIfNull(allow);
        ArgumentNullException.ThrowIfNull(revoke);
        ArgumentNullException.ThrowIfNull(isAllowed);

        _allow = allow;
        _revoke = revoke;
        _isAllowed = isAllowed;

        // A row without a path - System, an exited process, or one this process may not inspect -
        // has no file a firewall rule could name, so neither choice is offered for it.
        _allowCommand = new RelayCommand(Allow, () => SelectedPath is { } path && !_isAllowed(path));
        _revokeCommand = new RelayCommand(Revoke, () => SelectedPath is { } path && _isAllowed(path));
    }

    public IAppRow? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                RefreshCommands();
            }
        }
    }

    public System.Windows.Input.ICommand AllowCommand => _allowCommand;

    public System.Windows.Input.ICommand RevokeCommand => _revokeCommand;

    public string Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(HasMessage));
            }
        }
    }

    public bool HasMessage => _message.Length > 0;

    private string? SelectedPath => string.IsNullOrWhiteSpace(_selected?.FullPath) ? null : _selected.FullPath;

    /// <summary>
    /// Re-reads whether the selection is allowed. Called when the context menu opens, because the
    /// allow-list can change on its own page while the same row stays selected here.
    /// </summary>
    public void RefreshCommands()
    {
        _allowCommand.RaiseCanExecuteChanged();
        _revokeCommand.RaiseCanExecuteChanged();
    }

    private void Allow()
    {
        if (_selected is { } row && SelectedPath is { } path)
        {
            Message = _allow(path, row.AppName);
            RefreshCommands();
        }
    }

    private void Revoke()
    {
        if (SelectedPath is { } path)
        {
            Message = _revoke(path);
            RefreshCommands();
        }
    }
}
