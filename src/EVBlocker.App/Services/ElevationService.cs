using System.Diagnostics;
using System.Security.Principal;

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
    private static readonly Lazy<bool> Elevated = new(DetectElevation);

    public static bool IsElevated => Elevated.Value;

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

    private static bool DetectElevation()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
