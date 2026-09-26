using EVBlocker.Core.Usb;

namespace EVBlocker.Core.Tests;

/// <summary>
/// What separates an infected drive from a clean one. The false positives matter as much as the
/// detections: a scanner that calls every drive infected gets ignored, and then stops working.
/// </summary>
public sealed class UsbThreatRulesTests
{
    [Fact]
    public void ShortcutNamedAfterAHiddenFolder_IsCertain()
    {
        // The whole trick in one drive listing.
        var entries = new[]
        {
            Folder("Photos", hidden: true, system: true),
            File("Photos.lnk"),
        };

        UsbFinding finding = Assert.Single(
            UsbThreatRules.Inspect(entries),
            f => f.Path.EndsWith(".lnk", StringComparison.Ordinal));

        Assert.Equal(ThreatConfidence.Certain, finding.Confidence);
        Assert.True(finding.Quarantine);
    }

    [Fact]
    public void ShortcutRunningAScriptHost_IsCertain()
    {
        var entries = new[]
        {
            File("Tai lieu.lnk") with
            {
                Link = new ShellLinkTarget { Arguments = "/c start wscript.exe boot.vbs" },
            },
        };

        UsbFinding finding = Assert.Single(UsbThreatRules.Inspect(entries));

        Assert.Equal(ThreatConfidence.Certain, finding.Confidence);
        Assert.Contains("wscript.exe", finding.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void OrdinaryShortcut_IsNotReported()
    {
        var entries = new[]
        {
            File("Bao cao.lnk") with
            {
                Link = new ShellLinkTarget { RelativePath = @"..\Tai lieu\Bao cao.docx" },
            },
        };

        Assert.Empty(UsbThreatRules.Inspect(entries));
    }

    [Fact]
    public void HiddenExecutable_IsSuspicious()
    {
        var entries = new[] { File("x.vbs", hidden: true, system: true) };

        UsbFinding finding = Assert.Single(UsbThreatRules.Inspect(entries));

        Assert.Equal(ThreatConfidence.Suspicious, finding.Confidence);
        Assert.True(finding.Quarantine);
    }

    [Fact]
    public void VisibleExecutable_IsNotReported()
    {
        // Installers and portable tools live on USB drives for honest reasons.
        Assert.Empty(UsbThreatRules.Inspect(new[] { File("rufus-4.15.exe") }));
    }

    [Fact]
    public void AutorunInf_IsSuspicious()
    {
        UsbFinding finding = Assert.Single(UsbThreatRules.Inspect(new[] { File("autorun.inf") }));

        Assert.Equal(ThreatConfidence.Suspicious, finding.Confidence);
    }

    /// <summary>
    /// Measured on a clean external disk: its only hidden system folders were these two, plus
    /// three more left by macOS. Reporting them would mean every drive reads as infected.
    /// </summary>
    [Theory]
    [InlineData("System Volume Information")]
    [InlineData("$RECYCLE.BIN")]
    [InlineData("RECYCLER")]
    [InlineData(".Spotlight-V100")]
    [InlineData(".Trashes")]
    [InlineData(".TemporaryItems")]
    [InlineData("EFI")]
    public void HiddenSystemFoldersWindowsAndMacOsMake_AreNotReported(string name)
    {
        Assert.Empty(UsbThreatRules.Inspect(new[] { Folder(name, hidden: true, system: true) }));
    }

    [Fact]
    public void OtherHiddenFolder_IsANoticeAndIsNotQuarantined()
    {
        UsbFinding finding = Assert.Single(
            UsbThreatRules.Inspect(new[] { Folder("Tai lieu", hidden: true, system: true) }));

        Assert.Equal(ThreatConfidence.Notice, finding.Confidence);

        // Moving the user's own folder into quarantine would be the tool doing the damage.
        Assert.False(finding.Quarantine);
    }

    [Fact]
    public void FolderHiddenButNotSystem_IsNotReported()
    {
        // Plain hidden is something people do on purpose; this family sets both.
        Assert.Empty(UsbThreatRules.Inspect(new[] { Folder("private", hidden: true, system: false) }));
    }

    /// <summary>The drive this was written against: real contents, and nothing to report.</summary>
    [Fact]
    public void CleanDrive_ProducesNothing()
    {
        var entries = new[]
        {
            Folder("Ventoy"),
            Folder("Games"),
            Folder("Setup"),
            Folder("Linux"),
            Folder("System Volume Information", hidden: true, system: true),
            Folder("$RECYCLE.BIN", hidden: true, system: true),
            Folder(".Spotlight-V100", hidden: true, system: false),
            Folder(".Trashes", hidden: true, system: false),
            File("ubuntu-26.04.1-desktop-amd64.iso"),
            File("rufus-4.15.exe"),
            File("._ubuntu-26.04.1-desktop-amd64.iso", hidden: true, system: false),
        };

        Assert.Empty(UsbThreatRules.Inspect(entries));
    }

    private static DriveEntry Folder(string name, bool hidden = false, bool system = false) =>
        new()
        {
            Name = name,
            FullPath = @"E:\" + name,
            IsDirectory = true,
            IsHidden = hidden,
            IsSystem = system,
        };

    private static DriveEntry File(string name, bool hidden = false, bool system = false) =>
        new()
        {
            Name = name,
            FullPath = @"E:\" + name,
            IsDirectory = false,
            IsHidden = hidden,
            IsSystem = system,
        };
}
