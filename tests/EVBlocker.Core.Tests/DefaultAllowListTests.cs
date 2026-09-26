using EVBlocker.Core.Policy;

namespace EVBlocker.Core.Tests;

/// <summary>
/// The catalogue is seeded into the allow-list on every load, so these decide what "default"
/// means: present unless the user has settled the question, either way.
/// </summary>
public sealed class DefaultAllowListTests
{
    private static readonly DiscoveredApp Chrome = new("Google Chrome", @"C:\Apps\chrome.exe");
    private static readonly DiscoveredApp Zalo = new("Zalo", @"C:\Apps\Zalo.exe");

    [Fact]
    public void EmptyList_TakesEveryDiscoveredApp()
    {
        IReadOnlyList<DiscoveredApp> missing =
            DefaultAllowList.MissingFrom(new AllowListDocument(), new[] { Chrome, Zalo });

        Assert.Equal(new[] { "Google Chrome", "Zalo" }, missing.Select(app => app.Name));
    }

    [Fact]
    public void AppAlreadyAllowed_IsNotOfferedAgain()
    {
        var document = new AllowListDocument
        {
            Apps = { new AllowedApp { ExecutablePath = Chrome.ExecutablePath } },
        };

        IReadOnlyList<DiscoveredApp> missing =
            DefaultAllowList.MissingFrom(document, new[] { Chrome, Zalo });

        Assert.Equal(new[] { "Zalo" }, missing.Select(app => app.Name));
    }

    [Fact]
    public void AppTheUserRemoved_StaysRemoved()
    {
        var document = new AllowListDocument { RemovedDefaults = { Zalo.ExecutablePath } };

        IReadOnlyList<DiscoveredApp> missing =
            DefaultAllowList.MissingFrom(document, new[] { Chrome, Zalo });

        Assert.Equal(new[] { "Google Chrome" }, missing.Select(app => app.Name));
    }

    /// <summary>
    /// Windows paths are case-insensitive, so a removal recorded in one casing has to match the
    /// catalogue's. Otherwise a removed default returns at the next load and looks like a bug.
    /// </summary>
    [Fact]
    public void PathComparison_IgnoresCase()
    {
        var document = new AllowListDocument
        {
            Apps = { new AllowedApp { ExecutablePath = @"c:\apps\CHROME.EXE" } },
            RemovedDefaults = { @"C:\APPS\zalo.exe" },
        };

        Assert.Empty(DefaultAllowList.MissingFrom(document, new[] { Chrome, Zalo }));
    }

    [Fact]
    public void NothingDiscovered_AddsNothing()
    {
        Assert.Empty(DefaultAllowList.MissingFrom(new AllowListDocument(), Array.Empty<DiscoveredApp>()));
    }
}
