using EVBlocker.Core.Firewall;
using EVBlocker.Core.Policy;

namespace EVBlocker.Core.Tests;

public sealed class PolicyApplierTests
{
    private const string ChromePath = @"C:\Program Files\Google\Chrome\chrome.exe";
    private const string EdgePath = @"C:\Program Files\Microsoft\Edge\msedge.exe";

    private static AllowListDocument AllowList(params string[] paths) => new()
    {
        Apps = paths.Select(p => new AllowedApp
        {
            ExecutablePath = p,
            DisplayName = Path.GetFileName(p),
            AddedAt = DateTimeOffset.UnixEpoch,
        }).ToList(),
    };

    private static FirewallRuleSpec ForeignRule(string name) => new()
    {
        Name = name,
        Group = "SomeOtherProduct",
        ApplicationPath = @"C:\other\tool.exe",
        Direction = FirewallDirection.Outbound,
        Action = FirewallAction.Allow,
    };

    [Fact]
    public void BuildDesired_ProducesOneOutboundAllowPerApp()
    {
        IReadOnlyList<FirewallRuleSpec> desired = PolicyApplier.BuildDesired(AllowList(ChromePath, EdgePath));

        Assert.Equal(2, desired.Count);
        Assert.All(desired, rule =>
        {
            Assert.Equal(FirewallDirection.Outbound, rule.Direction);
            Assert.Equal(FirewallAction.Allow, rule.Action);
            Assert.Equal(FirewallProfiles.All, rule.Profiles);
            Assert.Equal(FirewallRuleNaming.Group, rule.Group);
            Assert.True(rule.Enabled);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildDesired_SkipsBlankPaths(string path)
    {
        // A rule with no application scope is a rule that allows everything, which is the exact
        // opposite of what an allow-list entry means.
        Assert.Empty(PolicyApplier.BuildDesired(AllowList(path)));
    }

    [Fact]
    public void BuildDesired_CollapsesDuplicateEntries()
    {
        // Two rules sharing one name would make removal by name ambiguous forever.
        Assert.Single(PolicyApplier.BuildDesired(AllowList(ChromePath, ChromePath)));
    }

    [Fact]
    public void Plan_EmptyFirewall_AddsEverything()
    {
        var policy = new FakeFirewallPolicy();

        PolicyDiff diff = new PolicyApplier(policy).Plan(AllowList(ChromePath, EdgePath));

        Assert.Equal(2, diff.ToAdd.Count);
        Assert.Empty(diff.ToRemove);
        Assert.Empty(diff.Unchanged);
        Assert.True(diff.HasChanges);
    }

    [Fact]
    public void Plan_AlreadyCorrect_ReportsNoChanges()
    {
        var policy = new FakeFirewallPolicy();
        policy.Seed(PolicyApplier.BuildDesired(AllowList(ChromePath)).ToArray());

        PolicyDiff diff = new PolicyApplier(policy).Plan(AllowList(ChromePath));

        Assert.Empty(diff.ToAdd);
        Assert.Empty(diff.ToRemove);
        Assert.Single(diff.Unchanged);
        Assert.False(diff.HasChanges);
    }

    [Fact]
    public void Plan_RuleForAppNoLongerAllowed_IsRemoved()
    {
        var policy = new FakeFirewallPolicy();
        policy.Seed(PolicyApplier.BuildDesired(AllowList(ChromePath, EdgePath)).ToArray());

        PolicyDiff diff = new PolicyApplier(policy).Plan(AllowList(ChromePath));

        Assert.Empty(diff.ToAdd);
        Assert.Single(diff.ToRemove);
        Assert.Contains("msedge.exe", diff.ToRemove[0].Name, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_RuleEditedOutsideTheApp_IsRewritten()
    {
        // Someone disabled our rule in wf.msc. Drift, not agreement.
        var policy = new FakeFirewallPolicy();
        FirewallRuleSpec correct = PolicyApplier.BuildDesired(AllowList(ChromePath))[0];
        policy.Seed(correct with { Enabled = false });

        PolicyDiff diff = new PolicyApplier(policy).Plan(AllowList(ChromePath));

        Assert.Single(diff.ToAdd);
        Assert.Empty(diff.ToRemove);
        Assert.Empty(diff.Unchanged);
    }

    [Fact]
    public void Plan_RuleTurnedIntoABlock_IsRewritten()
    {
        var policy = new FakeFirewallPolicy();
        FirewallRuleSpec correct = PolicyApplier.BuildDesired(AllowList(ChromePath))[0];
        policy.Seed(correct with { Action = FirewallAction.Block });

        Assert.Single(new PolicyApplier(policy).Plan(AllowList(ChromePath)).ToAdd);
    }

    [Fact]
    public void Plan_PathCasingDiffersOnly_IsNotTreatedAsDrift()
    {
        // Windows paths are case-insensitive. Reporting drift here would make the applier
        // rewrite the same rule on every single run, forever.
        var policy = new FakeFirewallPolicy();
        FirewallRuleSpec correct = PolicyApplier.BuildDesired(AllowList(ChromePath))[0];
        policy.Seed(correct with { ApplicationPath = ChromePath.ToUpperInvariant() });

        PolicyDiff diff = new PolicyApplier(policy).Plan(AllowList(ChromePath));

        Assert.Empty(diff.ToAdd);
        Assert.Single(diff.Unchanged);
    }

    [Fact]
    public void Plan_IgnoresRulesOutsideOurGroup()
    {
        var policy = new FakeFirewallPolicy();
        policy.Seed(ForeignRule("Some other product - allow tool"));

        PolicyDiff diff = new PolicyApplier(policy).Plan(AllowList());

        Assert.Empty(diff.ToRemove);
        Assert.Empty(diff.ToAdd);
    }

    [Fact]
    public void Apply_RemovesBeforeAdding()
    {
        // A rename has to free the old name before the new rule claims it.
        var policy = new FakeFirewallPolicy();
        policy.Seed(PolicyApplier.BuildDesired(AllowList(EdgePath)).ToArray());

        new PolicyApplier(policy).Apply(AllowList(ChromePath));

        int removeAt = policy.Calls.FindIndex(c => c.StartsWith("Remove:", StringComparison.Ordinal));
        int addAt = policy.Calls.FindIndex(c => c.StartsWith("Add:", StringComparison.Ordinal));

        Assert.True(removeAt >= 0 && addAt >= 0);
        Assert.True(removeAt < addAt, "Removals must run before additions.");
    }

    [Fact]
    public void Apply_IsIdempotent()
    {
        var policy = new FakeFirewallPolicy();
        var applier = new PolicyApplier(policy);
        AllowListDocument list = AllowList(ChromePath, EdgePath);

        applier.Apply(list);
        int writesAfterFirst = policy.WriteCount;
        PolicyDiff second = applier.Apply(list);

        Assert.False(second.HasChanges);
        Assert.Equal(writesAfterFirst, policy.WriteCount);
    }

    [Fact]
    public void Apply_NothingToDo_WritesNothing()
    {
        var policy = new FakeFirewallPolicy();

        new PolicyApplier(policy).Apply(AllowList());

        Assert.Equal(0, policy.WriteCount);
    }

    [Fact]
    public void Apply_RefusesToRemoveARuleFromAnotherGroup()
    {
        // The firewall implementation is supposed to filter by group; this guards the case where
        // it does not, because the cost of being wrong is deleting someone else's rule.
        var policy = new MisbehavingPolicy();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => new PolicyApplier(policy).Apply(AllowList()));

        Assert.Contains("SomeOtherProduct", error.Message, StringComparison.Ordinal);
        Assert.Empty(policy.Removed);
    }

    /// <summary>Returns a rule from another group, which GetRulesInGroup should never do.</summary>
    private sealed class MisbehavingPolicy : IFirewallPolicy
    {
        public List<string> Removed { get; } = new();

        public IReadOnlyList<FirewallRuleSpec> GetRulesInGroup(string group) =>
            new[] { ForeignRule("Not ours") };

        public void AddRule(FirewallRuleSpec rule)
        {
        }

        public void RemoveRule(string name) => Removed.Add(name);

        // Not part of what this fake exists to misbehave about.
        public IReadOnlyDictionary<FirewallProfile, FirewallAction> GetDefaultOutboundActions() =>
            new Dictionary<FirewallProfile, FirewallAction>();

        public void SetDefaultOutboundAction(FirewallProfile profile, FirewallAction action) =>
            throw new NotSupportedException();
    }
}
