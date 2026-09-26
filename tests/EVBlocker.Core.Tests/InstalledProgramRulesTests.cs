using EVBlocker.Core.Installed;

namespace EVBlocker.Core.Tests;

/// <summary>
/// The registry says what is installed; it does not say which file opens a socket. These cover
/// the decisions that close that gap, which is where this view can quietly go wrong.
/// </summary>
public sealed class InstalledProgramRulesTests
{
    [Fact]
    public void OrdinaryEntry_ShowsInControlPanel()
    {
        Assert.True(InstalledProgramRules.ShowsInControlPanel(
            new UninstallEntry { DisplayName = "Google Chrome" }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EntryWithoutAName_IsHidden(string? name)
    {
        Assert.False(InstalledProgramRules.ShowsInControlPanel(
            new UninstallEntry { DisplayName = name }));
    }

    [Fact]
    public void SystemComponent_IsHidden()
    {
        Assert.False(InstalledProgramRules.ShowsInControlPanel(
            new UninstallEntry { DisplayName = "Some plumbing", SystemComponent = 1 }));
    }

    [Fact]
    public void SubPackageOfAnotherEntry_IsHidden()
    {
        Assert.False(InstalledProgramRules.ShowsInControlPanel(
            new UninstallEntry { DisplayName = "A part", ParentKeyName = "TheParent" }));
    }

    [Fact]
    public void UpdateEntry_IsHidden()
    {
        // Control Panel lists these on its separate updates page, not with the programs.
        Assert.False(InstalledProgramRules.ShowsInControlPanel(
            new UninstallEntry { DisplayName = "Security Update", ReleaseType = "Update" }));
    }

    [Theory]
    [InlineData(@"C:\Apps\app.exe,0", @"C:\Apps\app.exe")]
    [InlineData(@"C:\Apps\app.exe", @"C:\Apps\app.exe")]
    [InlineData(@"""C:\Program Files\A, B\app.exe""", @"C:\Program Files\A, B\app.exe")]
    [InlineData(@"""C:\Apps\app.exe"",0", @"C:\Apps\app.exe")]
    public void DisplayIcon_YieldsTheExecutableItNames(string icon, string expected)
    {
        Assert.Equal(expected, InstalledProgramRules.TryGetIconExecutable(icon));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\Apps\app.dll,3")]
    [InlineData(@"C:\Apps\icon.ico")]
    public void DisplayIcon_ThatIsNotAnExecutable_YieldsNothing(string? icon)
    {
        Assert.Null(InstalledProgramRules.TryGetIconExecutable(icon));
    }

    [Theory]
    [InlineData(@"C:\ProgramData\Package Cache\{guid}\python-3.14.6-amd64.exe")]
    [InlineData(@"C:\Program Files\Microsoft OneDrive\26.168\OneDriveSetup.exe")]
    [InlineData(@"C:\Apps\setup.exe")]
    [InlineData(@"C:\Apps\unins000.exe")]
    [InlineData(@"C:\Apps\Uninstaller.exe")]
    [InlineData(@"C:\ProgramData\Package Cache\{guid}\VC_redist.x64.exe")]
    public void InstallerImages_AreRecognised(string path)
    {
        Assert.True(InstalledProgramRules.IsInstallerImage(path));
    }

    [Theory]
    [InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe")]
    [InlineData(@"C:\Program Files\Microsoft OneDrive\OneDrive.exe")]
    public void RealApplications_AreNotMistakenForInstallers(string path)
    {
        Assert.False(InstalledProgramRules.IsInstallerImage(path));
    }

    /// <summary>
    /// A deliberate false positive, recorded so it is a decision rather than a surprise. An
    /// application whose name begins with "setup" is treated as an installer and its row asks
    /// the user to pick the file instead - visible, and fixable with the manual add button.
    /// Letting an installer through is the worse half of the trade: it writes a rule for a file
    /// that runs once and never opens a socket, and nothing about the list looks wrong after.
    /// </summary>
    [Fact]
    public void NameBeginningWithSetup_IsTreatedAsAnInstaller()
    {
        Assert.True(InstalledProgramRules.IsInstallerImage(@"C:\Apps\SetupDesigner.exe"));
    }

    /// <summary>
    /// The case the whole resolution step exists for: ten helpers sit beside the installer and
    /// the one that syncs is a directory above, so a plain listing allows the wrong ten files.
    /// </summary>
    [Fact]
    public void MatchingName_PicksTheProgramsOwnExecutable()
    {
        string[] found =
        {
            @"C:\Program Files\Microsoft OneDrive\26.168\FileCoAuth.exe",
            @"C:\Program Files\Microsoft OneDrive\26.168\FileSyncConfig.exe",
            @"C:\Program Files\Microsoft OneDrive\OneDrive.exe",
            @"C:\Program Files\Microsoft OneDrive\OneDriveStandaloneUpdater.exe",
        };

        Assert.Equal(
            new[] { @"C:\Program Files\Microsoft OneDrive\OneDrive.exe" },
            InstalledProgramRules.PreferMatchingName("Microsoft OneDrive", found));
    }

    [Fact]
    public void MatchingName_IgnoresPunctuationAndCase()
    {
        string[] found = { @"C:\Apps\SmartLab.App.exe", @"C:\Apps\helper.exe" };

        Assert.Equal(
            new[] { @"C:\Apps\SmartLab.App.exe" },
            InstalledProgramRules.PreferMatchingName("Smart Lab", found));
    }

    [Fact]
    public void MatchingName_WhenNothingMatches_KeepsEverything()
    {
        // A weak answer beats none: the caller still caps how many it will take.
        string[] found = { @"C:\Apps\alpha.exe", @"C:\Apps\beta.exe" };

        Assert.Equal(found, InstalledProgramRules.PreferMatchingName("Totally Different", found));
    }

    [Theory]
    [InlineData("20260926", 2026, 9, 26)]
    public void InstallDate_IsParsedFromTheDocumentedShape(string value, int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), InstalledProgramRules.ParseInstallDate(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("26/09/2026")]
    [InlineData("notadate")]
    public void InstallDate_InAnyOtherShape_IsNull(string? value)
    {
        Assert.Null(InstalledProgramRules.ParseInstallDate(value));
    }

    [Theory]
    [InlineData("Microsoft Visual C++ v14 Redistributable (x64)")]
    [InlineData("Intel(R) Chipset Device Software")]
    [InlineData("Realtek Ethernet Controller Driver")]
    [InlineData("Windows Software Development Kit")]
    public void PlumbingEntries_AreMarkedAsSupportComponents(string name)
    {
        Assert.True(InstalledProgramRules.IsSupportComponent(name));
    }

    [Theory]
    [InlineData("Google Chrome")]
    [InlineData("Zalo 26.7.10")]
    [InlineData("Microsoft Visual Studio Code (User)")]
    public void RealApplications_AreNotMarkedAsSupportComponents(string name)
    {
        Assert.False(InstalledProgramRules.IsSupportComponent(name));
    }
}
