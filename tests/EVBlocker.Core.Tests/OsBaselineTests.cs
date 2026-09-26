using EVBlocker.Core.Baseline;
using EVBlocker.Core.Firewall;

namespace EVBlocker.Core.Tests;

public sealed class OsBaselineTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"evblocker-baseline-{Guid.NewGuid():N}");

    private string OverrideFile => Path.Combine(_directory, "baseline-allow.json");

    /// <summary>A path that does not exist, so the shipped embedded baseline is used.</summary>
    private string NoOverride => Path.Combine(_directory, "absent.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private OsBaseline WithOverride(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(OverrideFile, json);
        return new OsBaseline(OverrideFile);
    }

    [Fact]
    public void ShippedBaseline_LoadsAndValidates()
    {
        // The embedded list is the one that decides whether a machine keeps working after
        // default-deny is switched on, so it is validated as part of the build's test run.
        BaselineDocument document = new OsBaseline(NoOverride).Load();

        Assert.NotEmpty(document.Services);
        Assert.Equal(BaselineDocument.CurrentSchemaVersion, document.SchemaVersion);
    }

    [Theory]
    // The four without which a machine is not usable: names, addresses, patches, and the
    // certificate plumbing that TLS depends on.
    [InlineData("Dnscache")]
    [InlineData("Dhcp")]
    [InlineData("wuauserv")]
    [InlineData("cryptsvc")]
    public void ShippedBaseline_CoversTheEssentialServices(string service)
    {
        BaselineDocument document = new OsBaseline(NoOverride).Load();

        Assert.Contains(document.Services, s =>
            string.Equals(s.Service, service, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildRules_ScopesByServiceAndNeverByExecutable()
    {
        // These services share svchost.exe. A rule scoped to that path would allow every service
        // hosted in the same process, which is the exact failure the baseline exists to avoid.
        BaselineDocument document = new OsBaseline(NoOverride).Load();

        IReadOnlyList<FirewallRuleSpec> rules = OsBaseline.BuildRules(document);

        Assert.Equal(document.Services.Count, rules.Count);
        Assert.All(rules, rule =>
        {
            Assert.Null(rule.ApplicationPath);
            Assert.False(string.IsNullOrWhiteSpace(rule.ServiceName));
            Assert.Equal(FirewallDirection.Outbound, rule.Direction);
            Assert.Equal(FirewallAction.Allow, rule.Action);
            Assert.Equal(FirewallProfiles.All, rule.Profiles);
            Assert.Equal(FirewallRuleNaming.Group, rule.Group);
        });
    }

    [Fact]
    public void BuildRules_ProducesUniqueNames()
    {
        BaselineDocument document = new OsBaseline(NoOverride).Load();

        IReadOnlyList<FirewallRuleSpec> rules = OsBaseline.BuildRules(document);

        Assert.Equal(rules.Count, rules.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Load_OverrideFileWins()
    {
        OsBaseline baseline = WithOverride(
            """{"schemaVersion":1,"services":[{"service":"Custom","displayName":"C","reason":"R"}]}""");

        Assert.True(baseline.UsingOverride);
        Assert.Equal("Custom", Assert.Single(baseline.Load().Services).Service);
    }

    [Fact]
    public void Load_EmptyServiceList_Throws()
    {
        // An empty baseline plus default-deny is a machine with no network at all.
        OsBaseline baseline = WithOverride("""{"schemaVersion":1,"services":[]}""");

        Assert.Throws<InvalidDataException>(() => baseline.Load());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Load_BlankServiceName_Throws(string service)
    {
        // A rule with no scope allows everything.
        OsBaseline baseline = WithOverride(
            $$"""{"schemaVersion":1,"services":[{"service":"{{service}}","displayName":"X","reason":"R"}]}""");

        Assert.Throws<InvalidDataException>(() => baseline.Load());
    }

    [Theory]
    [InlineData("svc*")]
    [InlineData("sv?c")]
    public void Load_WildcardServiceName_Throws(string service)
    {
        OsBaseline baseline = WithOverride(
            $$"""{"schemaVersion":1,"services":[{"service":"{{service}}","displayName":"X","reason":"R"}]}""");

        Assert.Throws<InvalidDataException>(() => baseline.Load());
    }

    [Fact]
    public void Load_DuplicateService_Throws()
    {
        // Two rules would share one name, and removal by name could only ever delete one.
        OsBaseline baseline = WithOverride(
            """
            {"schemaVersion":1,"services":[
              {"service":"Dnscache","displayName":"A","reason":"R"},
              {"service":"dnscache","displayName":"B","reason":"R"}]}
            """);

        Assert.Throws<InvalidDataException>(() => baseline.Load());
    }

    [Fact]
    public void Load_MissingReason_Throws()
    {
        OsBaseline baseline = WithOverride(
            """{"schemaVersion":1,"services":[{"service":"Dnscache","displayName":"A","reason":""}]}""");

        Assert.Throws<InvalidDataException>(() => baseline.Load());
    }

    [Fact]
    public void Load_NewerSchema_Throws()
    {
        OsBaseline baseline = WithOverride(
            """{"schemaVersion":99,"services":[{"service":"Dnscache","displayName":"A","reason":"R"}]}""");

        Assert.Throws<InvalidDataException>(() => baseline.Load());
    }

    [Fact]
    public void Load_CorruptOverride_Throws()
    {
        // Falling back to the embedded list here would hide that the operator's edit is broken.
        OsBaseline baseline = WithOverride("{ not json");

        Assert.Throws<InvalidDataException>(() => baseline.Load());
    }
}
