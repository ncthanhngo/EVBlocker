using EVBlocker.Core.Policy;

namespace EVBlocker.Core.Tests;

public sealed class PathGlobTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"evblocker-glob-{Guid.NewGuid():N}");

    public PathGlobTests()
    {
        // Two version-named directories, the shape JetBrains and Google Drive both install into.
        Directory.CreateDirectory(Path.Combine(_root, "App", "1.0", "bin"));
        Directory.CreateDirectory(Path.Combine(_root, "App", "2.0", "bin"));
        File.WriteAllText(Path.Combine(_root, "App", "1.0", "bin", "tool.exe"), "old");
        File.WriteAllText(Path.Combine(_root, "App", "2.0", "bin", "tool.exe"), "new");
        File.WriteAllText(Path.Combine(_root, "plain.exe"), "x");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void ExactPath_IsReturnedWhenItExists()
    {
        Assert.Single(PathGlob.ExpandFiles(Path.Combine(_root, "plain.exe")));
    }

    [Fact]
    public void ExactPath_ThatDoesNotExist_ReturnsNothing()
    {
        // A path that is not there must never become a rule; Windows would accept it and it
        // would then allow nothing.
        Assert.Empty(PathGlob.ExpandFiles(Path.Combine(_root, "absent.exe")));
    }

    [Fact]
    public void Wildcard_MatchesEveryVersionDirectory()
    {
        IReadOnlyList<string> found = PathGlob.ExpandFiles(Path.Combine(_root, "App", "*", "bin", "tool.exe"));

        Assert.Equal(2, found.Count);
    }

    [Fact]
    public void Wildcard_ReturnsNewestFirst()
    {
        // A version pattern usually matches several installs; the recent one is the one in use.
        string newest = Path.Combine(_root, "App", "2.0", "bin", "tool.exe");
        File.SetLastWriteTimeUtc(newest, DateTime.UtcNow);
        File.SetLastWriteTimeUtc(
            Path.Combine(_root, "App", "1.0", "bin", "tool.exe"),
            DateTime.UtcNow.AddDays(-30));

        Assert.Equal(newest, PathGlob.ExpandFiles(Path.Combine(_root, "App", "*", "bin", "tool.exe"))[0]);
    }

    [Fact]
    public void Wildcard_MatchingNothing_ReturnsNothing()
    {
        Assert.Empty(PathGlob.ExpandFiles(Path.Combine(_root, "Nope", "*", "tool.exe")));
    }

    [Fact]
    public void UnexpandedEnvironmentVariable_ReturnsNothing()
    {
        // Matching literally against a path still containing %VAR% would only ever find nothing,
        // and would hide that the machine has no such location.
        Assert.Empty(PathGlob.ExpandFiles(@"%DEFINITELY_NOT_A_REAL_VARIABLE%\app.exe"));
    }

    [Fact]
    public void EnvironmentVariable_IsExpanded()
    {
        // %SystemRoot%\explorer.exe exists on every Windows install.
        Assert.Single(PathGlob.ExpandFiles(@"%SystemRoot%\explorer.exe"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankPattern_ReturnsNothing(string pattern)
    {
        Assert.Empty(PathGlob.ExpandFiles(pattern));
    }
}

public sealed class KnownAppsTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"evblocker-knownapps-{Guid.NewGuid():N}");

    /// <summary>A path that does not exist, so the shipped catalogue is used.</summary>
    private string NoOverride => Path.Combine(_directory, "absent.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void ShippedCatalogue_LoadsAndValidates()
    {
        KnownAppsDocument document = new KnownApps(NoOverride).Load();

        Assert.NotEmpty(document.Apps);
        Assert.Equal(KnownAppsDocument.CurrentSchemaVersion, document.SchemaVersion);
    }

    [Theory]
    [InlineData("Google Chrome")]
    [InlineData("Microsoft Edge")]
    [InlineData("Visual Studio Code")]
    [InlineData("GoLand")]
    [InlineData("Claude CLI")]
    [InlineData("Codex CLI")]
    [InlineData("OneDrive")]
    [InlineData("Google Drive")]
    [InlineData("Zalo")]
    public void ShippedCatalogue_ContainsTheRequestedApplications(string name)
    {
        Assert.Contains(new KnownApps(NoOverride).Load().Apps, a => a.Name == name);
    }

    [Fact]
    public void EveryEntry_HasANameAndSomewhereToLook()
    {
        Assert.All(new KnownApps(NoOverride).Load().Apps, app =>
        {
            Assert.False(string.IsNullOrWhiteSpace(app.Name));
            Assert.NotEmpty(app.Candidates);
            Assert.All(app.Candidates, c => Assert.False(string.IsNullOrWhiteSpace(c)));
        });
    }

    [Fact]
    public void Discover_ReturnsOnlyFilesThatExist()
    {
        // The whole point: a machine has some of these and not others, and an entry for a file
        // that is not there would become a rule that allows nothing.
        Assert.All(
            new KnownApps(NoOverride).Discover(),
            app => Assert.True(File.Exists(app.ExecutablePath), app.ExecutablePath));
    }

    [Fact]
    public void Discover_ReturnsAtMostOnePathPerApplication()
    {
        IReadOnlyList<DiscoveredApp> found = new KnownApps(NoOverride).Discover();

        Assert.Equal(found.Count, found.Select(a => a.Name).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void OverrideFile_ReplacesTheShippedCatalogue()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "known-apps.json");
        File.WriteAllText(
            path,
            """{"schemaVersion":1,"apps":[{"name":"Custom","candidates":["%SystemRoot%\\explorer.exe"]}]}""");

        var known = new KnownApps(path);

        Assert.True(known.UsingOverride);
        Assert.Equal("Custom", Assert.Single(known.Load().Apps).Name);
    }

    [Fact]
    public void CorruptOverride_Throws()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "known-apps.json");
        File.WriteAllText(path, "{ not json");

        Assert.Throws<InvalidDataException>(() => new KnownApps(path).Load());
    }
}
