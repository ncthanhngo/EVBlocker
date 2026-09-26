using System.Diagnostics;
using System.Threading.Tasks;
using EVBlocker.App.Mvvm;
using EVBlocker.App.Services;
using EVBlocker.App.Theming;

namespace EVBlocker.App.ViewModels;

/// <summary>Start-up, appearance and updates.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly RelayCommand _checkUpdateCommand;
    private readonly RelayCommand _openReleaseCommand;

    private string _updateStatus = "Chưa kiểm tra.";
    private string? _releaseUrl;
    private bool _checking;
    private bool _startWithWindows = UserSettingsStore.Load().StartWithWindows;

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

            // Loaded, changed, saved - not replaced. Writing a fresh object would discard every
            // other preference the file holds, which is a bug that only appears once a second
            // setting exists and is then very hard to attribute.
            UserSettings settings = UserSettingsStore.Load();
            settings.SetTheme(target);
            UserSettingsStore.Save(settings);

            OnPropertyChanged();
        }
    }

    /// <summary>Whether EVBlocker starts in the tray when the user signs in.</summary>
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (!SetProperty(ref _startWithWindows, value))
            {
                return;
            }

            UserSettings settings = UserSettingsStore.Load();
            settings.StartWithWindows = value;
            UserSettingsStore.Save(settings);

            LoginStartup.Apply(value);
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
        catch (Exception ex)
        {
            // This runs as a fire-and-forget task from a command. UpdateChecker handles the
            // failures it can name, but anything it does not would otherwise become an unobserved
            // task exception - invisible, and detached from the button that caused it.
            UpdateStatus = $"Lỗi khi kiểm tra bản mới: {ex.Message}";
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
