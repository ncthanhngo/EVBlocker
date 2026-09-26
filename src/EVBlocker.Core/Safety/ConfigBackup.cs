using System.Globalization;
using EVBlocker.Core.Internal;

namespace EVBlocker.Core.Safety;

/// <summary>One exported firewall configuration on disk.</summary>
public sealed record BackupInfo(string Path, DateTimeOffset CreatedAt);

/// <summary>
/// Exports and restores the whole Windows Firewall configuration.
/// </summary>
/// <remarks>
/// This is the escape hatch the enforcement phase is not allowed to proceed without. Turning on
/// default-deny outbound with a wrong baseline leaves a machine with no network, and at that
/// point the only thing that helps is a file taken before the change.
///
/// netsh is used rather than COM because the firewall COM API has no export or import: there is
/// no way to capture the whole configuration through it.
/// </remarks>
public sealed class ConfigBackup : IConfigBackup
{
    /// <summary>
    /// Windows' own extension for an exported policy, so the files are recognisable and can be
    /// imported by hand through wf.msc if this app is not available.
    /// </summary>
    private const string Extension = ".wfw";

    private const string FileNamePrefix = "firewall-";
    private const string TimestampFormat = "yyyyMMdd-HHmmss";

    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(60);

    private readonly string _directory;
    private readonly int _retain;

    public ConfigBackup(string directory, int retain = 10)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfLessThan(retain, 1);

        _directory = directory;
        _retain = retain;
    }

    public static string DefaultDirectory => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "EVBlocker",
        "backups");

    /// <summary>
    /// Exports the current configuration and prunes older backups.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Not running elevated.</exception>
    public BackupInfo Create() => Create(DateTimeOffset.Now);

    /// <summary>Overload taking the timestamp, so the file naming can be tested.</summary>
    internal BackupInfo Create(DateTimeOffset now)
    {
        Elevation.Require("export the firewall configuration");

        Directory.CreateDirectory(_directory);
        string path = System.IO.Path.Combine(_directory, BuildFileName(now));

        ProcessResult result = ProcessRunner.Run(
            "netsh.exe",
            new[] { "advfirewall", "export", path },
            CommandTimeout);

        // netsh reports success for some failures, so the file itself is the evidence.
        if (!result.Succeeded || !File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Firewall export failed (exit {result.ExitCode}). {Describe(result)}");
        }

        Prune();
        return new BackupInfo(path, now);
    }

    /// <summary>Backups in this directory, newest first.</summary>
    public IReadOnlyList<BackupInfo> List()
    {
        if (!Directory.Exists(_directory))
        {
            return Array.Empty<BackupInfo>();
        }

        return Directory.EnumerateFiles(_directory, $"{FileNamePrefix}*{Extension}")
            .Select(path => new { path, when = TryParseTimestamp(System.IO.Path.GetFileName(path)) })
            // A file that does not parse was not written by this class. Leaving it out keeps
            // pruning from deleting something a person put here deliberately.
            .Where(x => x.when is not null)
            .Select(x => new BackupInfo(x.path, x.when!.Value))
            .OrderByDescending(b => b.CreatedAt)
            .ToList();
    }

    /// <summary>
    /// Replaces the entire firewall configuration with a previously exported one.
    /// </summary>
    /// <remarks>
    /// Import is wholesale: every rule created since the export is gone afterwards, including
    /// rules other software added. Callers must say so before offering this.
    /// </remarks>
    public void Restore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Elevation.Require("restore the firewall configuration");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Backup file not found.", path);
        }

        ProcessResult result = ProcessRunner.Run(
            "netsh.exe",
            new[] { "advfirewall", "import", path },
            CommandTimeout);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Firewall import failed (exit {result.ExitCode}). {Describe(result)}");
        }
    }

    /// <summary>Deletes all but the newest <c>retain</c> backups. Returns how many were removed.</summary>
    public int Prune()
    {
        int removed = 0;

        foreach (BackupInfo old in List().Skip(_retain))
        {
            try
            {
                File.Delete(old.Path);
                removed++;
            }
            catch (IOException)
            {
                // Failing to tidy an old backup must never break the operation that triggered it.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return removed;
    }

    internal static string BuildFileName(DateTimeOffset when) =>
        FileNamePrefix + when.ToString(TimestampFormat, CultureInfo.InvariantCulture) + Extension;

    internal static DateTimeOffset? TryParseTimestamp(string fileName)
    {
        if (!fileName.StartsWith(FileNamePrefix, StringComparison.OrdinalIgnoreCase)
            || !fileName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string stamp = fileName[FileNamePrefix.Length..^Extension.Length];

        return DateTimeOffset.TryParseExact(
            stamp,
            TimestampFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out DateTimeOffset parsed)
            ? parsed
            : null;
    }

    private static string Describe(ProcessResult result)
    {
        string detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;

        return detail.Trim();
    }
}
