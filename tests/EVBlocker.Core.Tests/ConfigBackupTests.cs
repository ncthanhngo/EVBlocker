using EVBlocker.Core.Safety;

namespace EVBlocker.Core.Tests;

public sealed class ConfigBackupTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"evblocker-backups-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private void WriteBackup(DateTimeOffset when)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, ConfigBackup.BuildFileName(when)), "not a real export");
    }

    // Listing and pruning both order by the timestamp in the name, so a name that cannot be
    // parsed back is a backup that silently disappears from the list.
    [Theory]
    // Offsets that are not the machine's own. The name carries no offset, so writing it in the
    // caller's and reading it back as local would only agree by coincidence - which is exactly
    // what happened until CI ran this in a different zone.
    [InlineData(-8)]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(13)]
    public void FileName_RoundTripsRegardlessOfTheOffsetItWasGiven(int offsetHours)
    {
        var when = new DateTimeOffset(2026, 9, 26, 11, 5, 30, TimeSpan.FromHours(offsetHours));

        DateTimeOffset? parsed = ConfigBackup.TryParseTimestamp(ConfigBackup.BuildFileName(when));

        Assert.NotNull(parsed);
        Assert.Equal(when.LocalDateTime, parsed.Value.LocalDateTime);
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("firewall-nonsense.wfw")]
    [InlineData("firewall-20260926-110530.txt")]
    [InlineData("something-20260926-110530.wfw")]
    public void TryParseTimestamp_RejectsForeignNames(string fileName)
    {
        Assert.Null(ConfigBackup.TryParseTimestamp(fileName));
    }

    [Fact]
    public void List_NewestFirst()
    {
        var baseTime = new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.FromHours(7));
        WriteBackup(baseTime);
        WriteBackup(baseTime.AddHours(2));
        WriteBackup(baseTime.AddHours(1));

        IReadOnlyList<BackupInfo> list = new ConfigBackup(_directory).List();

        Assert.Equal(3, list.Count);
        Assert.True(list[0].CreatedAt > list[1].CreatedAt);
        Assert.True(list[1].CreatedAt > list[2].CreatedAt);
    }

    [Fact]
    public void List_IgnoresFilesItDidNotWrite()
    {
        // Pruning deletes from this list, so anything a person put in the folder by hand has to
        // stay out of it.
        WriteBackup(DateTimeOffset.Now);
        File.WriteAllText(Path.Combine(_directory, "keep-me.wfw"), "manual export");
        File.WriteAllText(Path.Combine(_directory, "readme.txt"), "notes");

        Assert.Single(new ConfigBackup(_directory).List());
    }

    [Fact]
    public void List_MissingDirectory_IsEmptyNotAnError()
    {
        Assert.Empty(new ConfigBackup(_directory).List());
    }

    [Fact]
    public void Prune_KeepsTheNewestAndDeletesTheRest()
    {
        var baseTime = new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.FromHours(7));
        for (int i = 0; i < 6; i++)
        {
            WriteBackup(baseTime.AddMinutes(i));
        }

        var backup = new ConfigBackup(_directory, retain: 2);
        int removed = backup.Prune();

        IReadOnlyList<BackupInfo> remaining = backup.List();
        Assert.Equal(4, removed);
        Assert.Equal(2, remaining.Count);
        Assert.Equal(baseTime.AddMinutes(5).LocalDateTime, remaining[0].CreatedAt.LocalDateTime);
    }

    [Fact]
    public void Prune_LeavesForeignFilesAlone()
    {
        WriteBackup(DateTimeOffset.Now);
        string manual = Path.Combine(_directory, "manual-export.wfw");
        File.WriteAllText(manual, "manual");

        new ConfigBackup(_directory, retain: 1).Prune();

        Assert.True(File.Exists(manual));
    }

    [Fact]
    public void Restore_MissingFile_Throws()
    {
        // Checked before elevation, so the message names the real problem.
        var backup = new ConfigBackup(_directory);

        Assert.ThrowsAny<Exception>(() => backup.Restore(Path.Combine(_directory, "absent.wfw")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsBlankDirectory(string directory)
    {
        Assert.Throws<ArgumentException>(() => new ConfigBackup(directory));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveRetention(int retain)
    {
        // Retaining zero backups would delete the only thing that can undo an enforcement change.
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConfigBackup(_directory, retain));
    }
}
