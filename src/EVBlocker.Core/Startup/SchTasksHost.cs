using System.Text;
using EVBlocker.Core.Internal;

namespace EVBlocker.Core.Startup;

/// <summary>
/// <see cref="IScheduledTaskHost"/> over schtasks.exe.
/// </summary>
/// <remarks>
/// schtasks rather than the Task Scheduler COM API. The usual reason to prefer COM - not parsing
/// localised console output - does not apply, because registering, removing and testing for a
/// task need only exit codes. That schtasks /Query returns 1 for a missing task regardless of
/// display language was measured, not assumed. The COM route would mean generating and wiring
/// roughly ten more interfaces for no behaviour this needs.
/// </remarks>
public sealed class SchTasksHost : IScheduledTaskHost
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    public bool Exists(string taskName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskName);

        return ProcessRunner
            .Run("schtasks.exe", new[] { "/Query", "/TN", taskName }, CommandTimeout)
            .Succeeded;
    }

    public void Register(string taskName, string taskXml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskName);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskXml);
        Elevation.Require($"register the scheduled task '{taskName}'");

        string xmlFile = Path.Combine(Path.GetTempPath(), $"evblocker-task-{Guid.NewGuid():N}.xml");

        try
        {
            // schtasks expects the definition as UTF-16.
            File.WriteAllText(xmlFile, taskXml, new UnicodeEncoding(bigEndian: false, byteOrderMark: true));

            ProcessResult result = ProcessRunner.Run(
                "schtasks.exe",
                new[] { "/Create", "/TN", taskName, "/XML", xmlFile, "/F" },
                CommandTimeout);

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not register scheduled task '{taskName}' (exit {result.ExitCode}). "
                    + result.FailureDetail);
            }
        }
        finally
        {
            SafeFile.TryDelete(xmlFile);
        }
    }

    public void Remove(string taskName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskName);
        Elevation.Require($"remove the scheduled task '{taskName}'");

        ProcessResult result = ProcessRunner.Run(
            "schtasks.exe",
            new[] { "/Delete", "/TN", taskName, "/F" },
            CommandTimeout);

        // A task that was not there is the desired end state too, so only a task that is still
        // present after the attempt counts as a failure.
        if (!result.Succeeded && Exists(taskName))
        {
            throw new InvalidOperationException(
                $"Could not remove scheduled task '{taskName}' (exit {result.ExitCode}). "
                + result.FailureDetail);
        }
    }


}
