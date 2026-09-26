using System.Security;

namespace EVBlocker.Core.Startup;

/// <summary>
/// The boot-time task that re-applies the policy when something has changed it.
/// </summary>
/// <remarks>
/// This task does not make blocking work. Blocking lives in the firewall configuration and is
/// applied by MpsSvc, which starts long before any user-mode program; the policy is already in
/// force by the time anything here could run. What this catches is drift - an installer, Group
/// Policy, another tool, or a Windows reset removing the rules the policy depends on. With
/// outbound blocked and the baseline rules gone, a machine has no DNS and no updates, and nothing
/// would put them back.
///
/// Registered as a scheduled task with an at-startup trigger rather than a Run key or the Startup
/// folder. Those run at logon, after a great deal has already started, and they run as the user -
/// so the elevation this needs would mean a UAC prompt at every sign-in. A task running as SYSTEM
/// runs before anyone logs in and prompts nobody.
/// </remarks>
public sealed class StartupReconcileTask
{
    public const string TaskName = "EVBlocker-StartupReconcile";

    /// <summary>The switch that puts the application into headless reconcile mode.</summary>
    public const string ReconcileSwitch = "--reconcile";

    /// <summary>Well-known SID of LOCAL SYSTEM. Used instead of the name, which is localised.</summary>
    private const string LocalSystemSid = "S-1-5-18";

    private readonly IScheduledTaskHost _tasks;

    public StartupReconcileTask()
        : this(new SchTasksHost())
    {
    }

    public StartupReconcileTask(IScheduledTaskHost tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        _tasks = tasks;
    }

    public bool IsInstalled() => _tasks.Exists(TaskName);

    /// <summary>
    /// Registers the task to run <paramref name="executablePath"/> at startup.
    /// </summary>
    public void Install(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        if (!File.Exists(executablePath))
        {
            // A task pointing at a missing file fails silently every boot, and the drift it was
            // meant to catch goes on being uncorrected.
            throw new FileNotFoundException(
                "Refusing to register a startup task for an executable that does not exist.",
                executablePath);
        }

        _tasks.Register(TaskName, BuildTaskXml(executablePath));
    }

    public void Uninstall() => _tasks.Remove(TaskName);

    /// <summary>
    /// Builds the task definition. Pure, so the settings can be asserted in a test.
    /// </summary>
    /// <remarks>
    /// The delay after boot is deliberate. Running the instant the machine starts would race
    /// MpsSvc finishing its own work, and a reconcile that reads a half-applied configuration
    /// would see drift that is not there and rewrite rules for no reason.
    /// </remarks>
    internal static string BuildTaskXml(string executablePath)
    {
        string command = SecurityElement.Escape(executablePath) ?? string.Empty;

        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Author>EVBlocker</Author>
                <Description>Ap lai rule cua EVBlocker khi khoi dong, neu cau hinh bi thay doi ben ngoai.</Description>
              </RegistrationInfo>
              <Triggers>
                <BootTrigger>
                  <Enabled>true</Enabled>
                  <Delay>PT30S</Delay>
                </BootTrigger>
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
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <ExecutionTimeLimit>PT5M</ExecutionTimeLimit>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{command}</Command>
                  <Arguments>{ReconcileSwitch}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
    }
}
