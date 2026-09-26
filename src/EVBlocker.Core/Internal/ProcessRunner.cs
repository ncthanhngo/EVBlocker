using System.Diagnostics;
using System.Text;

namespace EVBlocker.Core.Internal;

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;

    /// <summary>
    /// Whatever the tool said about the failure, for an error message.
    /// </summary>
    /// <remarks>
    /// Console tools are inconsistent about which stream they complain on - netsh and auditpol
    /// both report some failures on stdout - so both are considered, stderr first.
    /// </remarks>
    public string FailureDetail =>
        (string.IsNullOrWhiteSpace(StandardError) ? StandardOutput : StandardError).Trim();
}

/// <summary>
/// Runs a console tool and captures its output.
///
/// Shelling out is a deliberate exception, not the default. It is used only where Windows offers
/// no usable API: audit policy (auditpol.exe) and firewall config export/import (netsh.exe).
/// Everything else in this project goes through P/Invoke or COM.
/// </summary>
internal static class ProcessRunner
{
    public static ProcessResult Run(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string argument in arguments)
        {
            // ArgumentList quotes each value itself, which avoids hand-built command-line
            // escaping bugs when a path contains spaces.
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        // Read both streams asynchronously: a tool that fills one pipe while this code waits on
        // the other would deadlock.
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            TryKill(process);
            throw new TimeoutException($"{fileName} did not exit within {timeout.TotalSeconds:0.#}s.");
        }

        // Parameterless WaitForExit after a timed wait flushes the async output handlers, which
        // otherwise may not have appended the final lines yet.
        process.WaitForExit();

        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited between the timeout and the kill; nothing to do.
        }
    }
}
