using EVBlocker.App.Mvvm;
using EVBlocker.App.Services;

namespace EVBlocker.App.ViewModels;

/// <summary>One machine in the SSH list, editable in place.</summary>
/// <remarks>
/// The connect command lives on the row rather than the panel so the grid's terminal button can
/// act on its own row without a parameterised command; it calls back to a shared launcher.
///
/// Edits write straight through to the underlying model, and the panel persists the whole list
/// when the row reports a change, so renaming a machine sticks without a separate save step.
/// </remarks>
public sealed class SshHostRowViewModel : ObservableObject
{
    private readonly Action<SshHostRowViewModel> _onChanged;

    public SshHostRowViewModel(SshHost model, Action<SshHost> connect, Action<SshHostRowViewModel> onChanged)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(connect);
        ArgumentNullException.ThrowIfNull(onChanged);

        Model = model;
        _onChanged = onChanged;
        ConnectCommand = new RelayCommand(() => connect(Model), () => Model.Address.Trim().Length > 0);
    }

    public SshHost Model { get; }

    public string DisplayName
    {
        get => Model.DisplayName;
        set
        {
            if (Model.DisplayName != value)
            {
                Model.DisplayName = value;
                OnPropertyChanged();
                _onChanged(this);
            }
        }
    }

    public string Address
    {
        get => Model.Address;
        set
        {
            if (Model.Address != value)
            {
                Model.Address = value;
                OnPropertyChanged();
                ((RelayCommand)ConnectCommand).RaiseCanExecuteChanged();
                _onChanged(this);
            }
        }
    }

    public string Username
    {
        get => Model.Username;
        set
        {
            if (Model.Username != value)
            {
                Model.Username = value;
                OnPropertyChanged();
                _onChanged(this);
            }
        }
    }

    public System.Windows.Input.ICommand ConnectCommand { get; }
}
