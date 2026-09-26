using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace EVBlocker.App;

public partial class App : Application
{
    /// <summary>
    /// A WinExe has no console, and an unhandled exception in one reaches Windows Error
    /// Reporting as a bucket id with no message. Writing the exception somewhere readable is the
    /// difference between a diagnosable crash and a support ticket that says "it closes".
    /// </summary>
    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EVBlocker",
        "crash.log");

    public App()
    {
        // Registered in the constructor, which runs before InitializeComponent, so a failure
        // loading the application resources is caught too. That is exactly the failure a XAML
        // typo produces, and the one hardest to diagnose without this.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Record(e.ExceptionObject as Exception);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Record(e.Exception);

        MessageBox.Show(
            $"EVBlocker gặp lỗi không xử lý được.\n\n{e.Exception.Message}\n\nChi tiết: {CrashLogPath}",
            "EVBlocker",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        // Left unhandled on purpose: a UI in an unknown state is not worth continuing into.
    }

    private static void Record(Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            File.AppendAllText(
                CrashLogPath,
                $"=== {DateTimeOffset.Now:O} ==={Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // The crash itself matters more than the record of it; never fail inside a handler.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
