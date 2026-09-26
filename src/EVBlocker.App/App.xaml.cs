using System.IO;
using System.Windows;
using System.Windows.Threading;
using EVBlocker.App.Services;
using EVBlocker.App.Theming;
using EVBlocker.App.Views;
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

        if (e.Args.Any(a => string.Equals(a, StartupReconcileTask.ReconcileSwitch, StringComparison.OrdinalIgnoreCase)))
        {
            Shutdown(RunReconcile());
            return;
        }

        // Applied before the window exists, so it opens in the chosen theme rather than flashing
        // the default one first.
        ThemeManager.Apply(UserSettingsStore.Load().Theme);

        new MainWindow().Show();
    }

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
