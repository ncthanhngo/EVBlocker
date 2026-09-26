using System.Globalization;

namespace EVBlocker.Core.Installed;

/// <summary>
/// The decisions made about an uninstall entry, with no registry or disk behind them.
/// </summary>
public static class InstalledProgramRules
{
    /// <summary>
    /// Words that mark a row as plumbing rather than something a person launches.
    /// </summary>
    /// <remarks>
    /// A visible, reversible display filter, not a classification anyone should trust. Drivers
    /// and redistributables fill most of the list and none of them open sockets, so hiding them
    /// by default is what makes the list usable - but the toggle is in the UI and the hidden
    /// count is shown, because a filter that quietly drops rows in a firewall tool is worse than
    /// a long list.
    /// </remarks>
    private static readonly string[] SupportComponentWords =
    {
        "redistributable", "runtime", "driver", "device software", "sdk", "chipset",
        "framework", "components", "add-in", "addon", "service pack", "update for",
        "development kit",
    };

    /// <summary>
    /// Whether Control Panel would list this entry on its Programs page.
    /// </summary>
    /// <remarks>
    /// The same four conditions Programs and Features applies. Reproducing them matters because
    /// the point of the view is to look like the list the user already knows; showing the raw
    /// keys would show roughly five times as many rows, most of them sub-packages.
    /// </remarks>
    public static bool ShowsInControlPanel(UninstallEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return !string.IsNullOrWhiteSpace(entry.DisplayName)
            && entry.SystemComponent == 0
            && string.IsNullOrEmpty(entry.ParentKeyName)
            && string.IsNullOrEmpty(entry.ReleaseType);
    }

    /// <summary>
    /// The executable named by <c>DisplayIcon</c>, or null when it does not name one.
    /// </summary>
    /// <remarks>
    /// The value is an icon reference, so it carries an optional <c>,index</c> suffix and may be
    /// quoted. It often points at a .dll or a .ico instead, which is why anything that is not an
    /// .exe is rejected rather than guessed at.
    /// </remarks>
    public static string? TryGetIconExecutable(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon))
        {
            return null;
        }

        string path = displayIcon.Trim();

        // A quoted path may itself contain a comma, so the index is only stripped from the tail
        // when the path is not quoted, or after the closing quote.
        if (path.StartsWith('"'))
        {
            int closing = path.IndexOf('"', 1);
            path = closing > 0 ? path[1..closing] : path[1..];
        }
        else
        {
            int comma = path.LastIndexOf(',');
            if (comma > 0)
            {
                path = path[..comma];
            }
        }

        path = path.Trim();

        return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    /// <summary>
    /// Whether a path names an installer or uninstaller rather than the program itself.
    /// </summary>
    /// <remarks>
    /// <c>DisplayIcon</c> points at one surprisingly often: measured on one machine it named
    /// <c>OneDriveSetup.exe</c> for OneDrive and the cached <c>python-…-amd64.exe</c> for Python.
    /// Allowing those writes a rule for a file that runs once at install time and never opens a
    /// socket - the failure this whole application exists to avoid, because the list still looks
    /// correct afterwards.
    ///
    /// The Package Cache test is not a guess: Windows Installer keeps the original package there
    /// so it can repair and uninstall, and nothing in that directory is ever the running program.
    /// The name tests are a heuristic, kept deliberately short.
    /// </remarks>
    public static bool IsInstallerImage(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (path.Contains(@"\Package Cache\", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string name = Path.GetFileNameWithoutExtension(path);

        return name.StartsWith("setup", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("unins", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("vc_redist", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("setup", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("uninstaller", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Narrows a directory listing to the executables whose name matches the program's.
    /// </summary>
    /// <remarks>
    /// A directory listing answers "what is installed here", not "what is this program", and the
    /// difference decides whether ticking a row works. OneDrive keeps ten binaries beside its
    /// installer and <c>OneDrive.exe</c> one directory up: taking the listing allows ten helpers
    /// and misses the process that actually syncs.
    ///
    /// Compared with everything but letters and digits removed, so "Microsoft OneDrive" matches
    /// "OneDrive.exe" while "OneDriveStandaloneUpdater.exe" is left out. Returns the input
    /// unchanged when nothing matches - a weak answer beats no answer, and the caller still
    /// applies its own ceiling.
    /// </remarks>
    public static IReadOnlyList<string> PreferMatchingName(
        string programName,
        IReadOnlyList<string> executables)
    {
        ArgumentNullException.ThrowIfNull(executables);

        string program = Simplify(programName);

        if (program.Length == 0)
        {
            return executables;
        }

        List<string> matching = executables
            .Where(path =>
            {
                string file = Simplify(Path.GetFileNameWithoutExtension(path));
                return file.Length > 0
                    && (program.Contains(file, StringComparison.Ordinal)
                        || file.Contains(program, StringComparison.Ordinal));
            })
            .ToList();

        return matching.Count > 0 ? matching : executables;
    }

    private static string Simplify(string? value) =>
        value is null
            ? string.Empty
            : string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    /// <summary>Whether the entry looks like a driver, runtime or redistributable.</summary>
    public static bool IsSupportComponent(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return false;
        }

        return SupportComponentWords.Any(
            word => displayName.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The install date, or null when the value is absent or not the documented shape.</summary>
    public static DateOnly? ParseInstallDate(string? installDate) =>
        DateOnly.TryParseExact(
            installDate,
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateOnly parsed)
            ? parsed
            : null;
}
