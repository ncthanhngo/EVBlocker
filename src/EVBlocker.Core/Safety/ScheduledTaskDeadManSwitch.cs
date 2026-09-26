using System.Globalization;
using System.Security;
using System.Text;
using EVBlocker.Core.Internal;

namespace EVBlocker.Core.Safety;

/// <summary>
/// Dead-man switch backed by a one-time Windows scheduled task running as SYSTEM.
/// </summary>
/// <remarks>
/// A scheduled task is the mechanism because it is the one that keeps its promise when the app
/// does not: it survives the app being killed, and with StartWhenAvailable it still runs after a
/// reboot that spanned the scheduled moment. An in-process timer would die alongside the thing it
/// is supposed to rescue.
///
/// schtasks.exe is used rather than the Task Scheduler COM API. The usual reason to prefer COM -
/// not parsing localised console output - does not apply here: creating, deleting and testing for
/// a task need only exit codes, and `schtasks /Query` returning 1 for a missing task was verified
/// to be locale-independent. The COM route would mean generating and wiring roughly ten more
/// interfaces for no behaviour this needs.
/// </remarks>
public sealed class ScheduledTaskDeadManSwitch : IDeadManSwitch
{
    /// <summary>
    /// Fixed name so a revert left behind by a previous run can be found and cancelled, rather
    /// than sitting in the task library waiting to undo something nobody remembers.
    /// </summary>
    public const string TaskName = "EVBlocker-DeadManRevert";

    /// <summary>Well-known SID of LOCAL SYSTEM. Used instead of the name, which is localised.</summary>
    private const string LocalSystemSid = "S-1-5-18";

    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Bounds on the delay. Too short and the user cannot finish checking whether the network
    /// still works; too long and a machine sits broken for the rest of the day.
    /// </summary>
    public static readonly TimeSpan MinimumDelay = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan MaximumDelay = TimeSpan.FromMinutes(60);

    public bool IsArmed()
    {
        // Exit 1 means "no such task"; verified to be the code regardless of display language.
        ProcessResult result = ProcessRunner.Run(
            "schtasks.exe",
            new[] { "/Query", "/TN", TaskName },
            CommandTimeout);

        return result.Succeeded;
    }

    public void Arm(string backupPath, TimeSpan delay)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);

        if (delay < MinimumDelay || delay > MaximumDelay)
        {
            throw new ArgumentOutOfRangeException(
                nameof(delay),
                delay,
                $"Delay must be between {MinimumDelay.TotalMinutes:0} and {MaximumDelay.TotalMinutes:0} minutes.");
        }

        Elevation.Require("schedule the automatic revert");

        if (!File.Exists(backupPath))
        {
            // Arming a revert that points at nothing is worse than not arming one: it reports
            // safety that does not exist.
            throw new FileNotFoundException(
                "Refusing to arm a revert for a backup that does not exist.", backupPath);
        }

        string xml = BuildTaskXml(backupPath, DateTimeOffset.Now + delay);
        string xmlFile = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"evblocker-deadman-{Guid.NewGuid():N}.xml");

        try
        {
            // schtasks expects the definition as UTF-16.
            File.WriteAllText(xmlFile, xml, new UnicodeEncoding(bigEndian: false, byteOrderMark: true));

            ProcessResult result = ProcessRunner.Run(
                "schtasks.exe",
                new[] { "/Create", "/TN", TaskName, "/XML", xmlFile, "/F" },
                CommandTimeout);

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not schedule the automatic revert (exit {result.ExitCode}). "
                    + $"{Describe(result)}");
            }
        }
        finally
        {
            TryDelete(xmlFile);
        }
    }

    public void Disarm()
    {
        Elevation.Require("cancel the automatic revert");

        ProcessResult result = ProcessRunner.Run(
            "schtasks.exe",
            new[] { "/Delete", "/TN", TaskName, "/F" },
            CommandTimeout);

        // Nothing to delete is the desired end state either way, so only a real failure throws.
        if (!result.Succeeded && IsArmed())
        {
            throw new InvalidOperationException(
                $"Could not cancel the automatic revert (exit {result.ExitCode}). {Describe(result)}");
        }
    }

    /// <summary>
    /// Builds the task definition.
    /// </summary>
    /// <remarks>
    /// Pure, so the settings that matter can be asserted in a test rather than discovered on a
    /// machine that failed to recover.
    ///
    /// StartWhenAvailable is the setting that makes this work across a reboot: without it, a task
    /// whose moment passed while the machine was off is simply skipped, and the revert never
    /// happens.
    /// </remarks>
    internal static string BuildTaskXml(string backupPath, DateTimeOffset fireAt)
    {
        string netsh = System.IO.Path.Combine(Environment.SystemDirectory, "netsh.exe");
        string arguments = SecurityElement.Escape($"advfirewall import \"{backupPath}\"") ?? string.Empty;
        string start = fireAt.LocalDateTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Author>EVBlocker</Author>
                <Description>Khoi phuc cau hinh Windows Firewall neu nguoi dung khong xac nhan.</Description>
              </RegistrationInfo>
              <Triggers>
                <TimeTrigger>
                  <StartBoundary>{start}</StartBoundary>
                  <Enabled>true</Enabled>
                </TimeTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{LocalSystemSid}</UserId>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <Enabled>true</Enabled>
                <StartWhenAvailable>true</StartWhenAvailable>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>false</AllowHardTerminate>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <ExecutionTimeLimit>PT5M</ExecutionTimeLimit>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{SecurityElement.Escape(netsh)}</Command>
                  <Arguments>{arguments}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static string Describe(ProcessResult result)
    {
        string detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;

        return detail.Trim();
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover temp file must not mask the outcome of arming the switch.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
