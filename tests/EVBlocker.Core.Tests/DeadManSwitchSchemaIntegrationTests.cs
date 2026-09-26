using System.Diagnostics;
using System.Text;
using EVBlocker.Core.Safety;
using EVBlocker.Core.Startup;

namespace EVBlocker.Core.Tests;

/// <summary>
/// Checks that Windows Task Scheduler actually accepts the definition this app generates.
/// </summary>
/// <remarks>
/// This one touches the machine: it registers a scheduled task and deletes it again. That is a
/// deliberate trade. Every other test asserts what the XML says; none of them can tell whether
/// Task Scheduler agrees it is a valid document, and the failure mode of finding out later is a
/// machine whose automatic recovery never ran.
///
/// The principal is rewritten from LOCAL SYSTEM to the current user so the task can be registered
/// without elevation. That leaves exactly one thing unverified - whether registering it as SYSTEM
/// is permitted - which is a permission question, not a schema one.
///
/// The task name is unique per run and removal happens in a finally, so a failed assertion cannot
/// leave a scheduled task behind.
/// </remarks>
public sealed class DeadManSwitchSchemaIntegrationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public void GeneratedXml_IsAcceptedByTaskScheduler() =>
        AssertAccepted(ScheduledTaskDeadManSwitch.BuildTaskXml(
            @"C:\ProgramData\EVBlocker\backups\test.wfw",
            DateTimeOffset.Now.AddMinutes(30),
            @"C:\Program Files\EVBlocker\EVBlocker.exe"));

    /// <remarks>
    /// The boot trigger is swapped for a time trigger: registering a boot trigger needs
    /// elevation, the same permission question the principal rewrite sidesteps. Everything else -
    /// settings, priority, actions - is checked as generated.
    /// </remarks>
    [Fact]
    public void StartupTaskXml_IsAcceptedByTaskScheduler()
    {
        string xml = StartupReconcileTask.BuildTaskXml(@"C:\Program Files\EVBlocker\EVBlocker.exe");
        int start = xml.IndexOf("<BootTrigger>", StringComparison.Ordinal);
        int end = xml.IndexOf("</BootTrigger>", StringComparison.Ordinal) + "</BootTrigger>".Length;
        string timeTrigger = $"<TimeTrigger><StartBoundary>{DateTime.Now.AddMinutes(30):yyyy-MM-ddTHH:mm:ss}</StartBoundary><Enabled>true</Enabled></TimeTrigger>";

        Assert.True(start >= 0 && end > start, "The startup task should trigger on boot.");
        AssertAccepted(string.Concat(xml.AsSpan(0, start), timeTrigger, xml.AsSpan(end)));
    }

    private static void AssertAccepted(string generated)
    {
        string taskName = $"EVBlocker-SchemaCheck-{Guid.NewGuid():N}";
        string xmlFile = Path.Combine(Path.GetTempPath(), $"{taskName}.xml");

        string xml = generated
            .Replace(
                "<UserId>S-1-5-18</UserId>",
                $"<UserId>{Environment.UserDomainName}\\{Environment.UserName}</UserId>",
                StringComparison.Ordinal)
            .Replace("<RunLevel>HighestAvailable</RunLevel>", "<RunLevel>LeastPrivilege</RunLevel>",
                StringComparison.Ordinal);

        try
        {
            File.WriteAllText(xmlFile, xml, new UnicodeEncoding(bigEndian: false, byteOrderMark: true));

            (int exitCode, string output) = Run("/Create", "/TN", taskName, "/XML", xmlFile, "/F");

            Assert.True(
                exitCode == 0,
                $"Task Scheduler rejected the generated definition (exit {exitCode}): {output}");

            // Registering is not the same as being there afterwards.
            Assert.Equal(0, Run("/Query", "/TN", taskName).ExitCode);
        }
        finally
        {
            Run("/Delete", "/TN", taskName, "/F");
            TryDelete(xmlFile);
        }
    }

    private static (int ExitCode, string Output) Run(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit((int)Timeout.TotalMilliseconds);

        return (process.ExitCode, output.Trim());
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
