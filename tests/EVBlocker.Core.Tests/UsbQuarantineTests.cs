using EVBlocker.Core.Usb;

namespace EVBlocker.Core.Tests;

/// <summary>
/// Quarantine moves other people's files. The property that matters is that every move can be
/// undone, because the rules that chose the files are heuristics.
/// </summary>
public sealed class UsbQuarantineTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"evblocker-quarantine-{Guid.NewGuid():N}");

    private string Drive => Path.Combine(_directory, "drive");

    private string Store => Path.Combine(_directory, "store");

    public UsbQuarantineTests()
    {
        Directory.CreateDirectory(Drive);
        Directory.CreateDirectory(Store);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            // Attributes set during a test would otherwise block the delete.
            foreach (FileSystemInfo item in new DirectoryInfo(_directory)
                         .EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
            {
                item.Attributes = FileAttributes.Normal;
            }

            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void QuarantinedFile_LeavesTheDriveAndIsKept()
    {
        string payload = WriteFile("evil.vbs", "wscript");

        CleanupResult result = new UsbQuarantine(Store).Apply(new[] { Threat(payload) });

        Assert.Equal(1, result.Quarantined);
        Assert.Empty(result.Failures);
        Assert.False(File.Exists(payload));

        // Kept, not destroyed: exactly one file waiting somewhere under the store.
        Assert.Single(Directory.GetFiles(Store, "*.vbs", SearchOption.AllDirectories));
    }

    [Fact]
    public void QuarantinedFile_CanBePutBack()
    {
        string payload = WriteFile("evil.vbs", "wscript");
        var quarantine = new UsbQuarantine(Store);

        quarantine.Apply(new[] { Threat(payload) });

        string batch = Directory.GetDirectories(Store).Single();
        QuarantineManifest manifest = UsbQuarantine.ReadManifest(batch)!;

        Assert.True(quarantine.Restore(batch, manifest.Items.Single()));
        Assert.True(File.Exists(payload));
        Assert.Equal("wscript", File.ReadAllText(payload));
    }

    [Fact]
    public void Manifest_RecordsWhereEachFileCameFromAndWhy()
    {
        string payload = WriteFile("autorun.inf", "[autorun]");

        new UsbQuarantine(Store).Apply(new[] { Threat(payload, "vì lý do này") });

        QuarantinedItem item = UsbQuarantine
            .ReadManifest(Directory.GetDirectories(Store).Single())!
            .Items
            .Single();

        Assert.Equal(payload, item.OriginalPath);
        Assert.Equal("vì lý do này", item.Reason);
    }

    /// <summary>
    /// The attributes are the reason the file was invisible, and a system file refuses to move
    /// while it still carries them.
    /// </summary>
    [Fact]
    public void HiddenSystemFile_IsStillMoved()
    {
        string payload = WriteFile("x.exe", "MZ");
        File.SetAttributes(payload, FileAttributes.Hidden | FileAttributes.System);

        Assert.Equal(1, new UsbQuarantine(Store).Apply(new[] { Threat(payload) }).Quarantined);
        Assert.False(File.Exists(payload));
    }

    [Fact]
    public void HiddenFolder_IsUnhiddenRatherThanMoved()
    {
        string folder = Path.Combine(Drive, "Tai lieu");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "bao cao.docx"), "x");
        new DirectoryInfo(folder).Attributes |= FileAttributes.Hidden | FileAttributes.System;

        CleanupResult result = new UsbQuarantine(Store).Apply(new[]
        {
            new UsbFinding
            {
                Path = folder,
                Confidence = ThreatConfidence.Notice,
                Reason = "bị ẩn",
                Quarantine = false,
            },
        });

        Assert.Equal(1, result.FoldersRestored);
        Assert.Equal(0, result.Quarantined);

        // Still there, and now visible - including what was inside it.
        Assert.True(Directory.Exists(folder));

        FileAttributes attributes = new DirectoryInfo(folder).Attributes;
        Assert.False(attributes.HasFlag(FileAttributes.Hidden));
        Assert.False(attributes.HasFlag(FileAttributes.System));
        Assert.False(File.GetAttributes(Path.Combine(folder, "bao cao.docx")).HasFlag(FileAttributes.Hidden));
    }

    [Fact]
    public void FileThatIsAlreadyGone_IsReportedRatherThanThrown()
    {
        CleanupResult result = new UsbQuarantine(Store)
            .Apply(new[] { Threat(Path.Combine(Drive, "khong-ton-tai.vbs")) });

        Assert.Equal(0, result.Quarantined);

        // A drive can be pulled mid-clean; the run has to survive it and say what it missed.
        Assert.Single(result.Failures);
    }

    [Fact]
    public void NothingToDo_TouchesNothing()
    {
        CleanupResult result = new UsbQuarantine(Store).Apply(Array.Empty<UsbFinding>());

        Assert.Equal(0, result.Quarantined);
        Assert.Empty(Directory.GetDirectories(Store));
    }

    private string WriteFile(string name, string content)
    {
        string path = Path.Combine(Drive, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static UsbFinding Threat(string path, string reason = "thử nghiệm") => new()
    {
        Path = path,
        Confidence = ThreatConfidence.Certain,
        Reason = reason,
        Quarantine = true,
    };
}
