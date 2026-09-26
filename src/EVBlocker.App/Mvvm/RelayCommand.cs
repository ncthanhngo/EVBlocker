using System.Windows.Input;

namespace EVBlocker.App.Mvvm;

/// <summary>ICommand backed by a delegate, with an optional enabled predicate.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();

    /// <summary>
    /// Called by the owning view model when the condition behind <c>canExecute</c> changed.
    /// Explicit rather than routed through CommandManager.RequerySuggested, which re-evaluates
    /// on every focus and keyboard event - wasteful next to a one-second poll.
    /// </summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
