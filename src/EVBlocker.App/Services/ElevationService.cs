using System.Diagnostics;

namespace EVBlocker.App.Services;

/// <summary>
/// Reports whether this process is elevated, and can relaunch it elevated.
/// </summary>
/// <remarks>
/// The manifest requests asInvoker, so the app starts unelevated and works in read-only form.
/// Reading the Security log and changing firewall policy both need administrator rights, so the
/// UI asks for a relaunch at the point those are wanted rather than at every launch.
/// </remarks>
public static class ElevationService
{
    /// <summary>
    /// Marks the elevated copy, which starts while this one is still exiting and so has to wait
    /// for the single-instance claim rather than hand over to the process it is replacing.
    /// </summary>
    public const string RelaunchSwitch = "--relaunched";

    /// <summary>
    /// Detection lives in Core, where the privileged work is, so the UI and the operations it
    /// drives can never disagree about whether this process is elevated.
    /// </summary>
    public static bool IsElevated => Core.Elevation.IsElevated;

    /// <summary>
    /// Starts an elevated copy and reports whether it was launched. Returns false when the user
    /// dismisses the UAC prompt, which is a normal choice and not an error.
    /// </summary>
    public static bool TryRelaunchElevated()
    {
        string? executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
        {
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = RelaunchSwitch,
            // "runas" is what raises the UAC prompt, and it requires the shell to start the
            // process, so UseShellExecute cannot be false here.
            Verb = "runas",
            UseShellExecute = true,
        };

        try
        {
            Process.Start(startInfo);
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Raised when the UAC prompt is cancelled.
            return false;
        }
    }
}
