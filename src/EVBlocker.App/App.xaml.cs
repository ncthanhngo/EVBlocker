using System.IO;
using System.Windows;
using System.Windows.Threading;
using EVBlocker.App.Services;
using EVBlocker.App.Theming;
using EVBlocker.App.Views;
using EVBlocker.Core.Firewall;
using EVBlocker.Core.Safety;
using EVBlocker.Core.Startup;

namespace EVBlocker.App;

public partial class App : Application
{
    /// <summary>
    /// A WinExe has no console, and an unhandled exception in one reaches Windows Error
    /// Reporting as a bucket id with no message. Writing the exception somewhere readable is the
    /// difference between a diagnosable crash and a support ticket that says "it closes".
    /// </summary>
    private static readonly string CrashLogPath = Path.Combine(DataDirectory, "crash.log");

    /// <summary>
    /// The reconcile run happens at boot, under SYSTEM, with nobody watching. A log is the only
    /// evidence that it ran at all, let alone what it decided.
    /// </summary>
    private static readonly string ReconcileLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "EVBlocker",
        "reconcile.log");

    private static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EVBlocker");

    private SingleInstance? _instance;
    private TrayIcon? _tray;
    private MainWindow? _window;

    public App()
    {
        // Registered in the constructor, which runs before InitializeComponent, so a failure
        // loading the application resources is caught too. That is exactly the failure a XAML
        // typo produces, and the one hardest to diagnose without this.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Record(CrashLogPath, e.ExceptionObject as Exception);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (HasSwitch(e, StartupReconcileTask.ReconcileSwitch))
        {
            Shutdown(RunReconcile());
            return;
        }

        if (HasSwitch(e, ScheduledTaskDeadManSwitch.RemoveBootGuardSwitch))
        {
            Shutdown(RemoveBootGuard());
            return;
        }

        TimeSpan claimWait = HasSwitch(e, ElevationService.RelaunchSwitch) ? TimeSpan.FromSeconds(15) : TimeSpan.Zero;
        _instance = SingleInstance.TryClaim(
            claimWait,
            () => Dispatcher.BeginInvoke(ShowMainWindow),
            out bool handedOver);

        if (_instance is null)
        {
            if (!handedOver)
            {
                MessageBox.Show(
                    "EVBlocker đang chạy với quyền quản trị. Mở nó từ biểu tượng ở khay hệ thống, góc phải thanh tác vụ.",
                    "EVBlocker",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            Shutdown();
            return;
        }

        // Closing the window hides it to the tray, so the last window closing is no longer the
        // end of the process; only an explicit exit is.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        UserSettings settings = UserSettingsStore.Load();

        // Applied before the window exists, so it opens in the chosen theme rather than flashing
        // the default one first.
        ThemeManager.Apply(settings.GetTheme());

#if !DEBUG
        // Re-applied every launch so the entry follows the executable if it has been moved. Not
        // in Debug builds, where it would point sign-in at whatever bin folder ran last.
        LoginStartup.Apply(settings.StartWithWindows);
#endif

        _tray = new TrayIcon(ShowMainWindow, ExitApplication);

        if (!HasSwitch(e, LoginStartup.TraySwitch))
        {
            ShowMainWindow();
        }
    }

    /// <summary>True once the process has decided to end, so closing the window really closes it.</summary>
    internal static bool IsExiting { get; private set; }

    /// <summary>Ends the process, as opposed to closing the window, which only hides it.</summary>
    internal static void ExitApplication()
    {
        IsExiting = true;
        Current.Shutdown();
    }

    /// <summary>Called by the window when it hides itself instead of closing.</summary>
    internal static void OnHiddenToTray() => ((App)Current)._tray?.ShowHiddenHint();

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Sign-out and shutdown must not be held up by a window that thinks it is only hiding.
        IsExiting = true;
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Brings the window back, creating it on first use: a launch at sign-in starts in the tray
    /// and should not pay for building and polling a window nobody has opened.
    /// </summary>
    private void ShowMainWindow()
    {
        _window ??= new MainWindow();

        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
    }

    private static bool HasSwitch(StartupEventArgs e, string name) =>
        e.Args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Headless reconcile. Returns the process exit code.
    /// </summary>
    /// <remarks>
    /// A non-zero code means the policy could not be read and nothing was changed, which is what
    /// Task Scheduler shows as the task's last result. It is the only signal available to someone
    /// checking whether boot-time reconciliation is working without reading the log.
    /// </remarks>
    private static int RunReconcile()
    {
        try
        {
            ReconcileReport report = CoreServices.CreateReconciler().Run();
            Append(ReconcileLogPath, report.Summary);

            return report.Outcome == ReconcileOutcome.Failed ? 1 : 0;
        }
        catch (Exception ex)
        {
            // Nothing above this catches, and an unhandled exception here would be invisible:
            // no console, no window, and a task result that says only "failed".
            Append(ReconcileLogPath, $"Unhandled: {ex}");
            return 1;
        }
    }

    /// <summary>
    /// Removes the boot guard and returns the exit code. Run by the dead-man revert, and by hand
    /// as the recovery step when a machine comes up without network.
    /// </summary>
    /// <remarks>
    /// Says what happened in a dialog only when a person ran it. Under SYSTEM, from the revert
    /// task, a dialog would wait forever on a desktop nobody can see.
    /// </remarks>
    private static int RemoveBootGuard()
    {
        bool byPerson = !System.Security.Principal.WindowsIdentity.GetCurrent().IsSystem;
        string message;
        int exitCode;

        try
        {
            new BootGuard().Remove();
            message = "Đã gỡ khoá lúc khởi động.";
            exitCode = 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
        {
            message = $"Không gỡ được khoá lúc khởi động: {ex.Message}";
            exitCode = 1;
        }

        Append(ReconcileLogPath, $"{ScheduledTaskDeadManSwitch.RemoveBootGuardSwitch}: {message}");

        if (byPerson)
        {
            MessageBox.Show(message, "EVBlocker", MessageBoxButton.OK,
                exitCode == 0 ? MessageBoxImage.Information : MessageBoxImage.Error);
        }

        return exitCode;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Record(CrashLogPath, e.Exception);

        MessageBox.Show(
            $"EVBlocker gặp lỗi không xử lý được.\n\n{e.Exception.Message}\n\nChi tiết: {CrashLogPath}",
            "EVBlocker",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        // Left unhandled on purpose: a UI in an unknown state is not worth continuing into.
    }

    private static void Record(string path, Exception? exception)
    {
        if (exception is not null)
        {
            Append(path, exception.ToString());
        }
    }

    private static void Append(string path, string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(
                path,
                $"=== {DateTimeOffset.Now:O} ==={Environment.NewLine}{message}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Whatever is being logged matters more than the record of it; never fail in here.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
