using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EVBlocker.Core.Usb;

/// <summary>One file that was moved out of harm's way, and where it came from.</summary>
public sealed record QuarantinedItem
{
    public string OriginalPath { get; init; } = string.Empty;

    public string StoredName { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;

    public DateTimeOffset MovedAt { get; init; }
}

public sealed class QuarantineManifest
{
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<QuarantinedItem> Items { get; set; } = new();

    public const int CurrentSchemaVersion = 1;
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(QuarantineManifest))]
internal sealed partial class QuarantineJsonContext : JsonSerializerContext
{
}

/// <summary>What one cleanup did.</summary>
public sealed record CleanupResult
{
    public required int Quarantined { get; init; }

    public required int FoldersRestored { get; init; }

    public required IReadOnlyList<string> Failures { get; init; }
}

/// <summary>
/// Moves suspicious files off a drive and un-hides what the worm hid.
/// </summary>
/// <remarks>
/// Moved, never deleted. These files sit on someone's drive next to their work, the rules that
/// picked them are heuristics, and one wrong call would destroy data with nothing to go back to.
/// Everything moved is recorded with where it came from, so a mistake is an inconvenience rather
/// than a loss.
/// </remarks>
public sealed class UsbQuarantine
{
    private const string ManifestName = "manifest.json";

    private readonly string _root;

    public UsbQuarantine()
        : this(DefaultRoot)
    {
    }

    public UsbQuarantine(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = root;
    }

    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "EVBlocker",
        "quarantine");

    /// <summary>
    /// Quarantines what should be quarantined and un-hides the folders the worm hid.
    /// </summary>
    public CleanupResult Apply(IReadOnlyList<UsbFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        var failures = new List<string>();
        var moved = new List<QuarantinedItem>();
        int restored = 0;

        string batch = Path.Combine(
            _root,
            DateTimeOffset.Now.LocalDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));

        foreach (UsbFinding finding in findings)
        {
            if (finding.Quarantine)
            {
                QuarantinedItem? item = TryMove(finding, batch, failures);

                if (item is not null)
                {
                    moved.Add(item);
                }
            }
            else if (finding.Confidence == ThreatConfidence.Notice)
            {
                restored += TryRestore(finding.Path, failures) ? 1 : 0;
            }
        }

        if (moved.Count > 0)
        {
            WriteManifest(batch, moved, failures);
        }

        return new CleanupResult
        {
            Quarantined = moved.Count,
            FoldersRestored = restored,
            Failures = failures,
        };
    }

    /// <summary>Puts one quarantined file back where it came from.</summary>
    public bool Restore(string batchDirectory, QuarantinedItem item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchDirectory);
        ArgumentNullException.ThrowIfNull(item);

        try
        {
            string stored = Path.Combine(batchDirectory, item.StoredName);
            string? directory = Path.GetDirectoryName(item.OriginalPath);

            if (!File.Exists(stored) || directory is null)
            {
                return false;
            }

            Directory.CreateDirectory(directory);
            File.Move(stored, item.OriginalPath, overwrite: false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Reads back what a batch holds, for showing or undoing it.</summary>
    public static QuarantineManifest? ReadManifest(string batchDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchDirectory);

        try
        {
            string path = Path.Combine(batchDirectory, ManifestName);

            return File.Exists(path)
                ? JsonSerializer.Deserialize(
                    File.ReadAllText(path),
                    QuarantineJsonContext.Default.QuarantineManifest)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private QuarantinedItem? TryMove(UsbFinding finding, string batch, List<string> failures)
    {
        try
        {
            Directory.CreateDirectory(batch);

            // Stored under a name of this application's choosing: two drives can both carry a
            // file called autorun.inf, and the original name is kept in the manifest anyway.
            string stored = $"{moveCounter():D3}-{Path.GetFileName(finding.Path)}";
            string destination = Path.Combine(batch, stored);

            // The attributes are what stopped the user seeing it; clearing them first is what
            // lets the move succeed on a read-only or system-flagged file.
            File.SetAttributes(finding.Path, FileAttributes.Normal);
            File.Move(finding.Path, destination, overwrite: false);

            return new QuarantinedItem
            {
                OriginalPath = finding.Path,
                StoredName = stored,
                Reason = finding.Reason,
                MovedAt = DateTimeOffset.Now,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failures.Add($"{finding.Path}: {ex.Message}");
            return null;
        }

        int moveCounter() => Directory.Exists(batch)
            ? Directory.GetFiles(batch).Length + 1
            : 1;
    }

    /// <summary>Clears Hidden and System from a directory and everything under it.</summary>
    /// <remarks>
    /// The job the batch file on the desktop did by hand, one folder per run, after asking the
    /// user to pick it out of a list and warning them not to pick the recovery partition. The
    /// rules decide which folders qualify, so nobody has to recognise them on sight.
    /// </remarks>
    private static bool TryRestore(string directory, List<string> failures)
    {
        try
        {
            var info = new DirectoryInfo(directory);

            if (!info.Exists)
            {
                return false;
            }

            Clear(info);

            foreach (FileSystemInfo child in info.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
            {
                Clear(child);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failures.Add($"{directory}: {ex.Message}");
            return false;
        }

        static void Clear(FileSystemInfo item) =>
            item.Attributes &= ~(FileAttributes.Hidden | FileAttributes.System);
    }

    private void WriteManifest(string batch, List<QuarantinedItem> moved, List<string> failures)
    {
        try
        {
            var manifest = new QuarantineManifest { Items = moved };

            File.WriteAllText(
                Path.Combine(batch, ManifestName),
                JsonSerializer.Serialize(manifest, QuarantineJsonContext.Default.QuarantineManifest));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The files are already moved. Saying so matters: without the manifest they can
            // still be found, but nothing records where they belong.
            failures.Add($"Không ghi được danh sách cách ly: {ex.Message}");
        }
    }
}
