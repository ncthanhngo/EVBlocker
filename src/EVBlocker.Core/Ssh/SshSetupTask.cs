using System.Security;
using EVBlocker.Core.Startup;

namespace EVBlocker.Core.Ssh;

/// <summary>
/// The boot-time task that turns SSH back on and re-authorises the admin key, under SYSTEM.
/// </summary>
/// <remarks>
/// The manual setup, or a first elevated run, could do this once - but a Windows update, a policy
/// sweep, or someone toggling the service would undo it, and nobody would notice until the machine
/// could no longer be reached. Running it at every boot makes the state self-healing, the same
/// reasoning as the policy reconcile task, and for the same reason it runs as SYSTEM: the work
/// needs administrator rights and nobody is at the keyboard when a machine starts.
/// </remarks>
public sealed class SshSetupTask
{
    public const string TaskName = "EVBlocker-SshSetup";

    /// <summary>The switch that runs the SSH setup headlessly and exits.</summary>
    public const string SetupSwitch = "--ssh-setup";

    private const string LocalSystemSid = "S-1-5-18";

    private readonly IScheduledTaskHost _tasks;

    public SshSetupTask()
        : this(new SchTasksHost())
    {
    }

    public SshSetupTask(IScheduledTaskHost tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        _tasks = tasks;
    }

    public bool IsInstalled() => _tasks.Exists(TaskName);

    public void Install(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                "Refusing to register an SSH setup task for an executable that does not exist.",
                executablePath);
        }

        _tasks.Register(TaskName, BuildTaskXml(executablePath));
    }

    public void Uninstall() => _tasks.Remove(TaskName);

    /// <summary>
    /// Builds the task definition. Pure, so the settings can be asserted in a test.
    /// </summary>
    /// <remarks>
    /// A short delay after boot: unlike the policy reconcile, nothing is offline waiting on this,
    /// and enabling the service can wait until the machine has settled. It only needs the network
    /// stack, so it does not race the firewall.
    /// </remarks>
    internal static string BuildTaskXml(string executablePath)
    {
        string command = SecurityElement.Escape(executablePath) ?? string.Empty;

        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Author>EVBlocker</Author>
                <Description>Bat SSH va cap phep khoa admin khi khoi dong, de may luon nhan duoc ket noi tu xa.</Description>
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
                <RunOnlyIfNetworkAvailable>true</RunOnlyIfNetworkAvailable>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <ExecutionTimeLimit>PT10M</ExecutionTimeLimit>
                <Priority>6</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{command}</Command>
                  <Arguments>{SetupSwitch}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
    }
}
