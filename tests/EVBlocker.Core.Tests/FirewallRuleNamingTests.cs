using EVBlocker.Core.Firewall;

namespace EVBlocker.Core.Tests;

public sealed class FirewallRuleNamingTests
{
    [Fact]
    public void ForApplication_IsDeterministic()
    {
        // Re-applying the policy must recognise the existing rule, not add a second one.
        Assert.Equal(
            FirewallRuleNaming.ForApplication(@"C:\app\a.exe", FirewallAction.Allow),
            FirewallRuleNaming.ForApplication(@"C:\app\a.exe", FirewallAction.Allow));
    }

    [Fact]
    public void ForApplication_SameFileNameDifferentFolders_ProducesDifferentNames()
    {
        // update.exe lives in every Electron app on the machine. Windows removes rules by name,
        // so a collision here would make removal ambiguous.
        string first = FirewallRuleNaming.ForApplication(@"C:\one\update.exe", FirewallAction.Allow);
        string second = FirewallRuleNaming.ForApplication(@"C:\two\update.exe", FirewallAction.Allow);

        Assert.NotEqual(first, second);
        Assert.Contains("update.exe", first, StringComparison.Ordinal);
        Assert.Contains("update.exe", second, StringComparison.Ordinal);
    }

    [Fact]
    public void ForApplication_CasingDoesNotChangeTheName()
    {
        // Windows paths are case-insensitive, so the same executable reached through differently
        // cased paths must not produce two rules.
        Assert.Equal(
            FirewallRuleNaming.ForApplication(@"C:\App\A.EXE", FirewallAction.Allow),
            FirewallRuleNaming.ForApplication(@"c:\app\a.exe", FirewallAction.Allow));
    }

    [Fact]
    public void ForApplication_ActionIsPartOfTheName()
    {
        Assert.NotEqual(
            FirewallRuleNaming.ForApplication(@"C:\app\a.exe", FirewallAction.Allow),
            FirewallRuleNaming.ForApplication(@"C:\app\a.exe", FirewallAction.Block));
    }

    [Fact]
    public void ForApplication_StartsWithTheGroupName()
    {
        // The prefix is what makes our rules findable by eye in wf.msc.
        Assert.StartsWith(
            FirewallRuleNaming.Group,
            FirewallRuleNaming.ForApplication(@"C:\app\a.exe", FirewallAction.Allow),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ForService_NamesTheService()
    {
        string name = FirewallRuleNaming.ForService("Dnscache", FirewallAction.Allow);

        Assert.Contains("Dnscache", name, StringComparison.Ordinal);
        Assert.StartsWith(FirewallRuleNaming.Group, name, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ForApplication_RejectsBlankPath(string path)
    {
        Assert.Throws<ArgumentException>(() => FirewallRuleNaming.ForApplication(path, FirewallAction.Allow));
    }
}
