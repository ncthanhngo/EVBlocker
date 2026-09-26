namespace EVBlocker.Core.Usb;

/// <summary>How sure the scanner is about one finding.</summary>
public enum ThreatConfidence
{
    /// <summary>Not a threat: something worth showing, such as a folder the user cannot see.</summary>
    Notice,

    /// <summary>Consistent with this family of worm, but explainable another way.</summary>
    Suspicious,

    /// <summary>Behaviour with no innocent explanation on removable media.</summary>
    Certain,
}

/// <summary>One thing found on a drive.</summary>
public sealed record UsbFinding
{
    public required string Path { get; init; }

    public required ThreatConfidence Confidence { get; init; }

    /// <summary>What was seen, in the words the interface shows.</summary>
    public required string Reason { get; init; }

    /// <summary>Whether moving this to quarantine is the right response.</summary>
    public required bool Quarantine { get; init; }
}

/// <summary>One entry on a drive, as the rules need to see it.</summary>
/// <remarks>
/// A flat description rather than FileSystemInfo so the rules can be tested without a disk. The
/// scanner fills it in from a real directory; a test fills it in by hand.
/// </remarks>
public sealed record DriveEntry
{
    public required string Name { get; init; }

    public required string FullPath { get; init; }

    public required bool IsDirectory { get; init; }

    public required bool IsHidden { get; init; }

    public required bool IsSystem { get; init; }

    /// <summary>Parsed shortcut contents, for a <c>.lnk</c> the scanner could read.</summary>
    public ShellLinkTarget? Link { get; init; }
}

/// <summary>
/// Decides what on a removable drive looks like a shortcut worm.
/// </summary>
public static class UsbThreatRules
{
    /// <summary>
    /// Directories that are hidden and system on healthy media.
    /// </summary>
    /// <remarks>
    /// Without this list every drive looks infected. Measured on one clean external disk: both
    /// of its hidden system folders were Windows' own, and three more hidden entries had been
    /// left by macOS. The batch file this feature replaces asked the user to pick from exactly
    /// this list by hand, and warned them not to choose the recovery partition.
    /// </remarks>
    private static readonly HashSet<string> BenignHiddenDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "System Volume Information", "$RECYCLE.BIN", "RECYCLER", "$Recycle.Bin",
        "found.000", "found.001", "EFI", "Recovery", "Config.Msi",
        // macOS leaves these on any drive it touches.
        ".Spotlight-V100", ".Trashes", ".TemporaryItems", ".fseventsd", ".DocumentRevisions-V100",
    };

    /// <summary>Programs that run whatever they are handed, which is why a worm points at them.</summary>
    private static readonly string[] ScriptHosts =
    {
        "cmd.exe", "command.com", "wscript.exe", "cscript.exe", "powershell.exe", "pwsh.exe",
        "mshta.exe", "rundll32.exe", "regsvr32.exe", "wmic.exe", "forfiles.exe", "conhost.exe",
    };

    /// <summary>Extensions Windows will execute if the user double-clicks them.</summary>
    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".scr", ".pif", ".bat", ".cmd", ".vbs", ".vbe", ".js", ".jse",
        ".wsf", ".wsh", ".hta", ".msi", ".ps1",
    };

    /// <summary>Everything the rules have to say about one directory's contents.</summary>
    public static IReadOnlyList<UsbFinding> Inspect(IReadOnlyList<DriveEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var findings = new List<UsbFinding>();

        HashSet<string> hiddenDirectoryNames = entries
            .Where(e => e.IsDirectory && e.IsHidden && e.IsSystem)
            .Select(e => e.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (DriveEntry entry in entries)
        {
            string extension = System.IO.Path.GetExtension(entry.Name);

            if (!entry.IsDirectory && string.Equals(entry.Name, "autorun.inf", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new UsbFinding
                {
                    Path = entry.FullPath,
                    Confidence = ThreatConfidence.Suspicious,
                    // Windows stopped honouring it on removable drives years ago, so its presence
                    // says something wrote it hoping for an older machine, not that it will run.
                    Reason = "Có autorun.inf — Windows đời mới không chạy nó, nhưng virus USB vẫn để lại",
                    Quarantine = true,
                });
            }

            if (!entry.IsDirectory && string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                UsbFinding? shortcut = InspectShortcut(entry, hiddenDirectoryNames);

                if (shortcut is not null)
                {
                    findings.Add(shortcut);
                }

                continue;
            }

            if (!entry.IsDirectory
                && entry.IsHidden
                && entry.IsSystem
                && ExecutableExtensions.Contains(extension))
            {
                findings.Add(new UsbFinding
                {
                    Path = entry.FullPath,
                    Confidence = ThreatConfidence.Suspicious,
                    Reason = "File chạy được nhưng bị đặt ẩn — không có lý do chính đáng trên USB",
                    Quarantine = true,
                });
            }

            if (entry.IsDirectory
                && entry.IsHidden
                && entry.IsSystem
                && !BenignHiddenDirectories.Contains(entry.Name))
            {
                findings.Add(new UsbFinding
                {
                    Path = entry.FullPath,
                    Confidence = ThreatConfidence.Notice,
                    Reason = "Thư mục bị đặt ẩn — có thể khôi phục lại cho hiện",
                    Quarantine = false,
                });
            }
        }

        return findings;
    }

    private static UsbFinding? InspectShortcut(DriveEntry entry, HashSet<string> hiddenDirectoryNames)
    {
        string bareName = System.IO.Path.GetFileNameWithoutExtension(entry.Name);

        // The signature move: hide the folder, leave a shortcut wearing its name. Anyone opening
        // the drive sees what looks like their folder and runs the payload by clicking it.
        if (hiddenDirectoryNames.Contains(bareName))
        {
            return new UsbFinding
            {
                Path = entry.FullPath,
                Confidence = ThreatConfidence.Certain,
                Reason = $"Shortcut trùng tên thư mục đang bị ẩn \"{bareName}\" — dấu hiệu virus USB",
                Quarantine = true,
            };
        }

        if (entry.Link is null)
        {
            return null;
        }

        string haystack = string.Join(
            ' ',
            entry.Link.RelativePath ?? string.Empty,
            entry.Link.Arguments ?? string.Empty,
            entry.Link.IconLocation ?? string.Empty);

        string? host = ScriptHosts.FirstOrDefault(
            h => haystack.Contains(h, StringComparison.OrdinalIgnoreCase));

        if (host is not null)
        {
            return new UsbFinding
            {
                Path = entry.FullPath,
                Confidence = ThreatConfidence.Certain,
                Reason = $"Shortcut chạy {host} kèm tham số — shortcut bình thường trỏ thẳng tới file",
                Quarantine = true,
            };
        }

        return null;
    }
}
