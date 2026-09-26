using Microsoft.Win32;

namespace EVBlocker.Core.Installed;

/// <summary>One program, as Control Panel would list it, with the executables found for it.</summary>
public sealed record InstalledProgram
{
    public required string Name { get; init; }

    public string? Publisher { get; init; }

    public string? Version { get; init; }

    public DateOnly? InstalledOn { get; init; }

    /// <summary>
    /// The executables this program was resolved to. Empty when none could be found.
    /// </summary>
    /// <remarks>
    /// Empty is a normal outcome, not an error: an uninstall entry is required to name an
    /// uninstaller, never an application. Node.js and GitHub CLI are both like this on the
    /// machine this was written on. Such a row is shown and says so, rather than being dropped -
    /// the user knows where those live and can add them by hand.
    /// </remarks>
    public required IReadOnlyList<string> Executables { get; init; }

    /// <summary>Whether this looks like a driver, runtime or redistributable.</summary>
    public required bool IsSupportComponent { get; init; }
}

public interface IInstalledProgramScanner
{
    IReadOnlyList<InstalledProgram> Scan();
}

/// <summary>
/// Reads the installed-programs list from the same place Control Panel reads it.
/// </summary>
/// <remarks>
/// Registry rather than a snapshot file, so a program installed a minute ago is in the next read
/// with nothing to refresh and nothing to keep in sync.
///
/// The gap this cannot close: an uninstall entry describes a *program*, and a firewall rule
/// scopes to an *executable*. Git is the clearest case - its entry resolves to the launchers in
/// the install root, while the binary that actually opens an HTTPS connection sits three
/// directories down. This view is for finding software, not for guaranteeing the right binary.
/// </remarks>
public sealed class InstalledProgramScanner : IInstalledProgramScanner
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>
    /// Ceiling on executables taken from an install directory.
    /// </summary>
    /// <remarks>
    /// Some install roots hold dozens. Allowing all of them because one was wanted is not a
    /// choice the user made, so a directory bigger than this contributes nothing and the row
    /// falls back to asking them to pick the file.
    /// </remarks>
    private const int MaxExecutablesPerProgram = 10;

    public IReadOnlyList<InstalledProgram> Scan()
    {
        var byName = new Dictionary<string, InstalledProgram>(StringComparer.CurrentCultureIgnoreCase);

        // Both registry views, because a 32-bit installer writes under WOW6432Node and a 64-bit
        // one does not; reading one view finds roughly half of what Control Panel shows.
        foreach ((RegistryHive hive, RegistryView view) in Roots())
        {
            foreach (UninstallEntry entry in ReadEntries(hive, view))
            {
                if (!InstalledProgramRules.ShowsInControlPanel(entry))
                {
                    continue;
                }

                var program = new InstalledProgram
                {
                    Name = entry.DisplayName!.Trim(),
                    Publisher = entry.Publisher,
                    Version = entry.DisplayVersion,
                    InstalledOn = InstalledProgramRules.ParseInstallDate(entry.InstallDate),
                    Executables = ResolveExecutables(entry),
                    IsSupportComponent = InstalledProgramRules.IsSupportComponent(entry.DisplayName),
                };

                // The same product often appears in more than one view or hive. Keep whichever
                // copy resolved to an executable; a duplicate row that cannot be ticked is worse
                // than no duplicate at all.
                if (!byName.TryGetValue(program.Name, out InstalledProgram? existing)
                    || (existing.Executables.Count == 0 && program.Executables.Count > 0))
                {
                    byName[program.Name] = program;
                }
            }
        }

        return byName.Values
            .OrderBy(program => program.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IEnumerable<(RegistryHive Hive, RegistryView View)> Roots()
    {
        yield return (RegistryHive.LocalMachine, RegistryView.Registry64);
        yield return (RegistryHive.LocalMachine, RegistryView.Registry32);
        yield return (RegistryHive.CurrentUser, RegistryView.Registry64);
    }

    private static IEnumerable<UninstallEntry> ReadEntries(RegistryHive hive, RegistryView view)
    {
        using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
        using RegistryKey? uninstall = baseKey.OpenSubKey(UninstallKey);

        if (uninstall is null)
        {
            yield break;
        }

        foreach (string name in uninstall.GetSubKeyNames())
        {
            UninstallEntry? entry = TryReadEntry(uninstall, name);

            if (entry is not null)
            {
                yield return entry;
            }
        }
    }

    private static UninstallEntry? TryReadEntry(RegistryKey uninstall, string subKeyName)
    {
        try
        {
            using RegistryKey? key = uninstall.OpenSubKey(subKeyName);

            if (key is null)
            {
                return null;
            }

            return new UninstallEntry
            {
                DisplayName = key.GetValue("DisplayName") as string,
                Publisher = key.GetValue("Publisher") as string,
                DisplayVersion = key.GetValue("DisplayVersion") as string,
                InstallLocation = key.GetValue("InstallLocation") as string,
                DisplayIcon = key.GetValue("DisplayIcon") as string,
                InstallDate = key.GetValue("InstallDate") as string,

                // Written as a DWORD by most installers and as a string by a few, so both shapes
                // are accepted; treating a string "1" as absent would show rows Control Panel hides.
                SystemComponent = key.GetValue("SystemComponent") switch
                {
                    int value => value,
                    string text when int.TryParse(text, out int value) => value,
                    _ => 0,
                },
                ParentKeyName = key.GetValue("ParentKeyName") as string,
                ReleaseType = key.GetValue("ReleaseType") as string,
            };
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            // A key this user cannot read contributes nothing, exactly like one that is not there.
            return null;
        }
    }

    private static IReadOnlyList<string> ResolveExecutables(UninstallEntry entry)
    {
        // DisplayIcon first: when an installer sets it to an .exe it is the program's own front
        // door, which is a far better answer than anything guessed from a directory listing.
        string? icon = InstalledProgramRules.TryGetIconExecutable(entry.DisplayIcon);

        if (icon is not null && !InstalledProgramRules.IsInstallerImage(icon) && File.Exists(icon))
        {
            return new[] { icon };
        }

        // Every candidate directory, not the first that yields something: the executable a
        // program is named after is regularly one level up from the one beside its installer.
        List<string> found = CandidateDirectories(entry, icon)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(ExecutablesIn)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        IReadOnlyList<string> narrowed =
            InstalledProgramRules.PreferMatchingName(entry.DisplayName ?? string.Empty, found);

        return narrowed.Count <= MaxExecutablesPerProgram
            ? narrowed
            : Array.Empty<string>();
    }

    /// <summary>Where to look for the program's executables, best guess first.</summary>
    /// <remarks>
    /// The directory holding the installer is worth searching because an installer named by
    /// DisplayIcon usually sits inside the tree it installed - OneDrive keeps its setup one
    /// level below the folder containing OneDrive.exe, which is how that entry resolves at all.
    /// A cached package is different: it lives away from the install, so it says nothing about
    /// where the program went.
    /// </remarks>
    private static IEnumerable<string> CandidateDirectories(UninstallEntry entry, string? iconExecutable)
    {
        if (!string.IsNullOrWhiteSpace(entry.InstallLocation))
        {
            yield return entry.InstallLocation.Trim();
        }

        if (iconExecutable is null
            || iconExecutable.Contains(@"\Package Cache\", StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        string? directory = Path.GetDirectoryName(iconExecutable);

        if (directory is null)
        {
            yield break;
        }

        yield return directory;

        // Climbing one level is worth it for the version-named directory installers sit in, but
        // never past an install root: the executables directly under Program Files belong to
        // whatever happens to live there, not to this program.
        string? parent = Path.GetDirectoryName(directory);

        if (parent is not null && !IsInstallRoot(parent))
        {
            yield return parent;
        }
    }

    private static bool IsInstallRoot(string directory) =>
        new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        }.Any(root => string.Equals(
            directory.TrimEnd(Path.DirectorySeparatorChar),
            root.TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase));

    private static string[] ExecutablesIn(string directory)
    {
        try
        {
            // Top level only. Recursing finds hundreds of helpers, updaters and crash handlers,
            // and allowing all of them is not what ticking one box should mean.
            return Directory.GetFiles(directory, "*.exe")
                .Where(path => !InstalledProgramRules.IsInstallerImage(path))
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Array.Empty<string>();
        }
    }
}
