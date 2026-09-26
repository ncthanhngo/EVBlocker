using System.Diagnostics;

namespace EVBlocker.Core.Monitor;

/// <summary>One executable that has at least one process running right now.</summary>
public sealed record RunningApp
{
    public required string ExecutablePath { get; init; }

    public required string Name { get; init; }

    /// <summary>How many processes are running this executable.</summary>
    public required int ProcessCount { get; init; }

    /// <summary>Whether any of them currently holds a connection to a public address.</summary>
    public required bool HasInternetConnection { get; init; }

    /// <summary>
    /// True for executables under the Windows directory.
    /// </summary>
    /// <remarks>
    /// Worth separating because allow-listing them is almost always the wrong move: the shared
    /// hosts live there, and allowing svchost.exe by path allows every service inside it. The
    /// OS baseline already covers those, scoped by service name.
    /// </remarks>
    public required bool IsWindowsComponent { get; init; }
}

/// <summary>What a scan found, and how much of the machine it could actually see.</summary>
public sealed record RunningAppScan
{
    public required IReadOnlyList<RunningApp> Apps { get; init; }

    public required int ProcessesSeen { get; init; }

    /// <summary>
    /// Processes that yielded no usable executable path - either it could not be read, or the
    /// file it names no longer exists.
    /// </summary>
    /// <remarks>
    /// Reported rather than swallowed. Without elevation this is most of them, and a list that
    /// silently omitted half the machine would look complete while being useless for deciding
    /// what to allow.
    /// </remarks>
    public required int ProcessesUnreadable { get; init; }
}

public interface IRunningAppScanner
{
    RunningAppScan Scan();
}

/// <summary>
/// Lists the executables running on this machine, so an allow-list can be built from what is
/// actually here rather than from memory.
/// </summary>
public sealed class RunningAppScanner : IRunningAppScanner
{
    private readonly IConnectionScanner _connections;

    public RunningAppScanner()
        : this(new ActiveConnectionScanner())
    {
    }

    public RunningAppScanner(IConnectionScanner connections)
    {
        ArgumentNullException.ThrowIfNull(connections);
        _connections = connections;
    }

    public RunningAppScan Scan()
    {
        // Taken first so the connection state describes the same moment as the process list.
        HashSet<string> connected = _connections.Scan()
            .Where(c => c.IsRemoteInternet && c.ExecutablePath is not null)
            .Select(c => c.ExecutablePath!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int seen = 0;
        int unreadable = 0;

        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                seen++;

                // QueryFullProcessImageName rather than Process.MainModule: the latter throws for
                // most processes without elevation and costs far more when it does not.
                string? path = ProcessPathResolver.TryGetPath(process.Id);

                // A path is also useless when the file behind it is gone. That happens more than
                // it sounds: an updater renames the old binary out of the way while the old
                // process is still running, so the process reports a path nothing is at any more.
                // Allow-listing one would write a rule that can never match again.
                if (path is null || !File.Exists(path))
                {
                    unreadable++;
                    continue;
                }

                counts[path] = counts.GetValueOrDefault(path) + 1;
            }
        }

        List<RunningApp> apps = counts
            .Select(entry => new RunningApp
            {
                ExecutablePath = entry.Key,
                Name = Path.GetFileName(entry.Key),
                ProcessCount = entry.Value,
                HasInternetConnection = connected.Contains(entry.Key),
                IsWindowsComponent = entry.Key.StartsWith(windowsDirectory, StringComparison.OrdinalIgnoreCase),
            })
            // Ones already talking to the internet first: they are the decisions that matter.
            .OrderByDescending(a => a.HasInternetConnection)
            .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new RunningAppScan
        {
            Apps = apps,
            ProcessesSeen = seen,
            ProcessesUnreadable = unreadable,
        };
    }
}
