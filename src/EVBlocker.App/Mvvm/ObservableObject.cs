using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace EVBlocker.App.Mvvm;

/// <summary>
/// Minimal INotifyPropertyChanged base.
/// </summary>
/// <remarks>
/// Hand-rolled rather than taken from an MVVM package: this is the only part of such a package
/// this app would use, and the published build is a self-contained single file where every
/// dependency shows up in the download size.
/// </remarks>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// Assigns and raises a change notification only when the value actually differs, so a poll
    /// that returns identical data does not churn the bindings.
    /// </summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
