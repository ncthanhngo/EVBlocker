namespace EVBlocker.Core.Usb;

/// <summary>What one drive scan found.</summary>
public sealed record UsbScanResult
{
    public required RemovableDrive Drive { get; init; }

    public required IReadOnlyList<UsbFinding> Findings { get; init; }

    /// <summary>Directories looked at, so a partial scan does not read as a clean one.</summary>
    public required int DirectoriesScanned { get; init; }

    /// <summary>Directories that could not be read, usually for want of rights.</summary>
    public required int DirectoriesUnreadable { get; init; }

    public bool IsInfected =>
        Findings.Any(f => f.Confidence is ThreatConfidence.Certain or ThreatConfidence.Suspicious);
}

public interface IUsbScanner
{
    UsbScanResult Scan(RemovableDrive drive);
}

/// <summary>
/// Walks a drive and applies the detection rules to what it finds.
/// </summary>
/// <remarks>
/// The root and one level below it, not the whole drive. This family works by what the user
/// sees when the drive opens, so it plants its shortcuts where they will be seen; walking a
/// terabyte disk to its leaves would take minutes and find nothing the root did not already say.
/// </remarks>
public sealed class UsbScanner : IUsbScanner
{
    /// <summary>How far below the root to look.</summary>
    private const int MaxDepth = 1;

    public UsbScanResult Scan(RemovableDrive drive)
    {
        ArgumentNullException.ThrowIfNull(drive);

        var findings = new List<UsbFinding>();
        int scanned = 0;
        int unreadable = 0;

        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((drive.Root, 0));

        while (queue.Count > 0)
        {
            (string path, int depth) = queue.Dequeue();

            IReadOnlyList<DriveEntry>? entries = TryRead(path);

            if (entries is null)
            {
                unreadable++;
                continue;
            }

            scanned++;
            findings.AddRange(UsbThreatRules.Inspect(entries));

            if (depth >= MaxDepth)
            {
                continue;
            }

            foreach (DriveEntry entry in entries.Where(e => e.IsDirectory))
            {
                queue.Enqueue((entry.FullPath, depth + 1));
            }
        }

        return new UsbScanResult
        {
            Drive = drive,
            Findings = findings,
            DirectoriesScanned = scanned,
            DirectoriesUnreadable = unreadable,
        };
    }

    private static IReadOnlyList<DriveEntry>? TryRead(string directory)
    {
        try
        {
            var entries = new List<DriveEntry>();

            foreach (FileSystemInfo item in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                bool isDirectory = item is DirectoryInfo;

                entries.Add(new DriveEntry
                {
                    Name = item.Name,
                    FullPath = item.FullName,
                    IsDirectory = isDirectory,
                    IsHidden = item.Attributes.HasFlag(FileAttributes.Hidden),
                    IsSystem = item.Attributes.HasFlag(FileAttributes.System),

                    // Read only for shortcuts, and only far enough to see the command line.
                    Link = !isDirectory
                           && string.Equals(item.Extension, ".lnk", StringComparison.OrdinalIgnoreCase)
                        ? ShellLink.TryRead(item.FullName)
                        : null,
                });
            }

            return entries;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Counted, never swallowed: a drive that could not be read is not a drive that is clean.
            return null;
        }
    }
}
