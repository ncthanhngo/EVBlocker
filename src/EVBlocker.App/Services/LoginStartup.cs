using System.IO;
using System.Security;
using Microsoft.Win32;

namespace EVBlocker.App.Services;

/// <summary>
/// Starts EVBlocker in the tray when the user signs in, through the per-user Run key.
/// </summary>
/// <remarks>
/// Run under HKCU rather than a scheduled task: it needs no administrator rights, it is the
/// place people look in Task Manager's Startup tab, and the app opens unelevated anyway.
/// This is a convenience for watching and editing; blocking itself is enforced by Windows
/// Firewall whether or not the app is running.
/// </remarks>
internal static class LoginStartup
{
    /// <summary>Starts hidden in the tray, which is what a launch at sign-in should do.</summary>
    public const string TraySwitch = "--tray";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string ValueName = "EVBlocker";

    /// <summary>
    /// Adds or removes the Run entry. Returns false when the registry refused the change.
    /// </summary>
    /// <remarks>
    /// The entry points at <see cref="Environment.ProcessPath"/>, the copy actually running, so
    /// applying it at every launch also repairs it after the executable has been moved.
    /// </remarks>
    public static bool Apply(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }

            string? executable = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executable))
            {
                return false;
            }

            key.SetValue(ValueName, $"\"{executable}\" {TraySwitch}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }
}
