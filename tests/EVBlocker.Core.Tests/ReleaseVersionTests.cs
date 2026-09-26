using EVBlocker.Core.Updates;

namespace EVBlocker.Core.Tests;

/// <summary>
/// Release tags are typed by hand, so they arrive in whatever shape somebody wrote. Getting this
/// wrong either hides a real update or nags about one that does not exist.
/// </summary>
public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("V1.2.3", 1, 2, 3)]
    [InlineData("  v1.2.3  ", 1, 2, 3)]
    [InlineData("v1.2", 1, 2, 0)]
    [InlineData("v2", 2, 0, 0)]
    [InlineData("v2.0.0-beta.1", 2, 0, 0)]
    [InlineData("1.4.0+build.77", 1, 4, 0)]
    [InlineData("v10.20.30", 10, 20, 30)]
    public void TryParse_ReadsTheNumericPart(string tag, int major, int minor, int build)
    {
        Assert.True(ReleaseVersion.TryParse(tag, out Version version));
        Assert.Equal(new Version(major, minor, build), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("v")]
    [InlineData("latest")]
    [InlineData("release-candidate")]
    public void TryParse_RejectsWhatIsNotAVersion(string? tag)
    {
        Assert.False(ReleaseVersion.TryParse(tag, out _));
    }

    [Theory]
    [InlineData("v1.0.1")]
    [InlineData("v1.1.0")]
    [InlineData("v2.0.0")]
    [InlineData("1.0.1")]
    public void IsNewerThan_HigherTag_IsAnUpdate(string tag)
    {
        Assert.True(ReleaseVersion.IsNewerThan(tag, new Version(1, 0, 0)));
    }

    [Theory]
    [InlineData("v1.0.0")]
    [InlineData("v0.9.9")]
    [InlineData("v1.0")]
    [InlineData("v1")]
    public void IsNewerThan_SameOrOlderTag_IsNotAnUpdate(string tag)
    {
        Assert.False(ReleaseVersion.IsNewerThan(tag, new Version(1, 0, 0)));
    }

    [Fact]
    public void IsNewerThan_UnreadableTag_IsNotAnUpdate()
    {
        // Telling somebody to upgrade because a tag could not be parsed would be worse than
        // staying quiet about it.
        Assert.False(ReleaseVersion.IsNewerThan("nightly", new Version(1, 0, 0)));
    }

    [Fact]
    public void IsNewerThan_IgnoresTheRevisionComponent()
    {
        // An assembly version is four-part and its revision moves on every build; comparing it
        // against a three-part tag would report an update on every single build.
        Assert.False(ReleaseVersion.IsNewerThan("v1.0.0", new Version(1, 0, 0, 4242)));
    }

    [Fact]
    public void IsNewerThan_ComparesNumerically_NotAsText()
    {
        // "10" sorts before "9" as text.
        Assert.True(ReleaseVersion.IsNewerThan("v1.10.0", new Version(1, 9, 0)));
    }
}
