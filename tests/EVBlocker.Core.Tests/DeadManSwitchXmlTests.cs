using System.Globalization;
using System.Xml.Linq;
using EVBlocker.Core.Safety;

namespace EVBlocker.Core.Tests;

/// <summary>
/// Asserts the scheduled-task definition.
/// </summary>
/// <remarks>
/// Every setting checked here is one whose absence turns a working safety net into a silent
/// no-op, and the failure only shows up on a machine that has already lost its network.
/// </remarks>
public sealed class DeadManSwitchXmlTests
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private const string BackupPath = @"C:\ProgramData\EVBlocker\backups\firewall-20260926-110530.wfw";

    private static XElement Parse(string? backupPath = null, DateTimeOffset? fireAt = null) =>
        XDocument.Parse(ScheduledTaskDeadManSwitch.BuildTaskXml(
            backupPath ?? BackupPath,
            fireAt ?? DateTimeOffset.Now.AddMinutes(10))).Root!;

    private static string? Value(XElement root, string name) =>
        root.Descendants(Ns + name).FirstOrDefault()?.Value;

    [Fact]
    public void Xml_IsWellFormed()
    {
        Assert.Equal(Ns + "Task", Parse().Name);
    }

    [Fact]
    public void RunsAsLocalSystem()
    {
        // By SID, not by name: "SYSTEM" is localised and would not resolve on every machine.
        Assert.Equal("S-1-5-18", Value(Parse(), "UserId"));
        Assert.Equal("HighestAvailable", Value(Parse(), "RunLevel"));
    }

    [Fact]
    public void StartWhenAvailable_IsOn()
    {
        // This is what makes the revert survive a reboot. Without it, a task whose moment passed
        // while the machine was off is skipped entirely and the network never comes back.
        Assert.Equal("true", Value(Parse(), "StartWhenAvailable"));
    }

    [Fact]
    public void BatterySettings_DoNotBlockTheRevert()
    {
        // A laptop on battery is exactly where somebody enables this and walks away.
        XElement root = Parse();

        Assert.Equal("false", Value(root, "DisallowStartIfOnBatteries"));
        Assert.Equal("false", Value(root, "StopIfGoingOnBatteries"));
    }

    [Fact]
    public void DoesNotWaitForANetwork()
    {
        // The policy being reverted may be the reason the machine appears to have no network.
        Assert.Equal("false", Value(Parse(), "RunOnlyIfNetworkAvailable"));
    }

    [Fact]
    public void TriggerFiresAtTheRequestedLocalTime()
    {
        var fireAt = new DateTimeOffset(2026, 9, 26, 11, 45, 0, TimeSpan.FromHours(7));

        string? start = Value(Parse(fireAt: fireAt), "StartBoundary");

        Assert.Equal(
            fireAt.LocalDateTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            start);
    }

    [Fact]
    public void ActionRestoresTheGivenBackup()
    {
        XElement root = Parse();

        Assert.EndsWith("netsh.exe", Value(root, "Command"), StringComparison.OrdinalIgnoreCase);

        string? arguments = Value(root, "Arguments");
        Assert.Contains("advfirewall import", arguments, StringComparison.Ordinal);
        Assert.Contains(BackupPath, arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandIsAnAbsolutePath()
    {
        // Resolving netsh through PATH under the SYSTEM account is one assumption too many.
        Assert.True(System.IO.Path.IsPathRooted(Value(Parse(), "Command")));
    }

    [Fact]
    public void PathWithXmlSpecialCharacters_StaysWellFormed()
    {
        // Folder names with an ampersand are ordinary and would otherwise break the document.
        const string awkward = @"C:\Backups\R&D (test) <v2>\firewall.wfw";

        XElement root = Parse(awkward);

        Assert.Contains(awkward, Value(root, "Arguments"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(61)]
    public void Arm_RejectsDelaysOutsideTheSafeRange(int minutes)
    {
        // Too short and nobody can finish checking the network; too long and a broken machine
        // stays broken for the rest of the day.
        var sut = new ScheduledTaskDeadManSwitch();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => sut.Arm(BackupPath, TimeSpan.FromMinutes(minutes)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Arm_RejectsBlankBackupPath(string path)
    {
        var sut = new ScheduledTaskDeadManSwitch();

        Assert.Throws<ArgumentException>(() => sut.Arm(path, TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void Arm_MissingBackup_IsRefused()
    {
        // Arming a revert that points at nothing is worse than not arming one: it reports safety
        // that does not exist.
        var sut = new ScheduledTaskDeadManSwitch(new FakeScheduledTaskHost());

        Assert.Throws<FileNotFoundException>(
            () => sut.Arm(Path.Combine(Path.GetTempPath(), "absent.wfw"), TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void ArmThenDisarm_RegistersAndRemovesTheTask()
    {
        var host = new FakeScheduledTaskHost();
        var sut = new ScheduledTaskDeadManSwitch(host);
        string backup = Path.Combine(Path.GetTempPath(), $"evb-{Guid.NewGuid():N}.wfw");
        File.WriteAllText(backup, "export");

        try
        {
            Assert.False(sut.IsArmed());

            sut.Arm(backup, TimeSpan.FromMinutes(10));
            Assert.True(sut.IsArmed());
            Assert.Contains(backup, host.XmlFor(ScheduledTaskDeadManSwitch.TaskName), StringComparison.Ordinal);

            sut.Disarm();
            Assert.False(sut.IsArmed());
        }
        finally
        {
            File.Delete(backup);
        }
    }

    [Fact]
    public void DeadManAndStartupTasks_UseDifferentNames()
    {
        // One name for both would mean arming a revert silently replaced the boot-time repair.
        Assert.NotEqual(
            ScheduledTaskDeadManSwitch.TaskName,
            EVBlocker.Core.Startup.StartupReconcileTask.TaskName);
    }
}
