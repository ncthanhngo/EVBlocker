using System.Diagnostics;
using System.Threading.Tasks;
using EVBlocker.App.Mvvm;
using EVBlocker.App.Services;
using EVBlocker.App.Theming;

namespace EVBlocker.App.ViewModels;

/// <summary>Appearance and updates.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly RelayCommand _checkUpdateCommand;
    private readonly RelayCommand _openReleaseCommand;

    private string _updateStatus = "Chưa kiểm tra.";
    private string? _releaseUrl;
    private bool _checking;

    public SettingsViewModel()
    {
        _checkUpdateCommand = new RelayCommand(
            () => _ = CheckForUpdateAsync(),
            () => !_checking);

        _openReleaseCommand = new RelayCommand(OpenReleasePage);
    }

    public string VersionText => $"Phiên bản {UpdateChecker.CurrentVersion.ToString(3)}";

    public bool IsDarkTheme
    {
        get => ThemeManager.Current == AppTheme.Dark;
        set
        {
            AppTheme target = value ? AppTheme.Dark : AppTheme.Light;
            if (ThemeManager.Current == target)
            {
                return;
            }

            ThemeManager.Apply(target);
            UserSettingsStore.Save(new UserSettings { Theme = target });
            OnPropertyChanged();
        }
    }

    public string UpdateStatus
    {
        get => _updateStatus;
        private set => SetProperty(ref _updateStatus, value);
    }

    public System.Windows.Input.ICommand CheckUpdateCommand => _checkUpdateCommand;

    public System.Windows.Input.ICommand OpenReleaseCommand => _openReleaseCommand;

    private async Task CheckForUpdateAsync()
    {
        _checking = true;
        _checkUpdateCommand.RaiseCanExecuteChanged();
        UpdateStatus = "Đang kiểm tra…";

        try
        {
            UpdateCheckResult result = await UpdateChecker.CheckAsync().ConfigureAwait(true);

            UpdateStatus = result.Message;
            _releaseUrl = result.ReleaseUrl;
        }
        finally
        {
            _checking = false;
            _checkUpdateCommand.RaiseCanExecuteChanged();
        }
    }

    private void OpenReleasePage()
    {
        // UseShellExecute so the machine's default browser handles it; without it, .NET treats
        // the URL as an executable path and fails.
        try
        {
            Process.Start(new ProcessStartInfo(_releaseUrl ?? UpdateChecker.ReleasesPageUrl)
            {
                UseShellExecute = true,
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            UpdateStatus = "Không mở được trình duyệt.";
        }
    }
}
