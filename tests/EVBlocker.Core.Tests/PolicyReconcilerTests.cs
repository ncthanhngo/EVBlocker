using EVBlocker.Core.Baseline;
using EVBlocker.Core.Firewall;
using EVBlocker.Core.Policy;
using EVBlocker.Core.Startup;

namespace EVBlocker.Core.Tests;

/// <summary>
/// The boot-time repair. What matters most here is what it refuses to do.
/// </summary>
public sealed class PolicyReconcilerTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"evblocker-reconcile-{Guid.NewGuid():N}");

    private readonly FakeFirewallPolicy _policy = new();
    private readonly FakeConfigBackup _backup = new();
    private readonly FakeDeadManSwitch _deadMan = new();

    private string StorePath => Path.Combine(_directory, "allowlist.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private EnforcementController Controller => new(
        _policy,
        _backup,
        _deadMan,
        new OsBaseline(Path.Combine(_directory, "no-baseline-override.json")));

    private PolicyReconciler Reconciler => new(Controller, _policy, new AllowListStore(StorePath));

    private void WriteAllowList(params string[] paths) =>
        new AllowListStore(StorePath).Save(new AllowListDocument
        {
            Apps = paths.Select(p => new AllowedApp
            {
                ExecutablePath = p,
                DisplayName = Path.GetFileName(p),
                AddedAt = DateTimeOffset.UnixEpoch,
            }).ToList(),
        });

    [Fact]
    public void Run_FirewallAlreadyCorrect_ReportsNoDriftAndWritesNothing()
    {
        WriteAllowList(@"C:\app\a.exe");
        Reconciler.Run();
        int writesAfterFirst = _policy.WriteCount;

        ReconcileReport report = Reconciler.Run();

        Assert.Equal(ReconcileOutcome.NoDrift, report.Outcome);
        Assert.Equal(writesAfterFirst, _policy.WriteCount);
    }

    [Fact]
    public void Run_RulesMissing_WritesThemBack()
    {
        // This is the case the whole feature exists for: with blocking on and the baseline rules
        // gone, the machine has no DNS and nothing else would put them back.
        WriteAllowList(@"C:\app\a.exe");

        ReconcileReport report = Reconciler.Run();

        Assert.Equal(ReconcileOutcome.Reapplied, report.Outcome);
        Assert.True(report.Added > 0);
        Assert.Contains(_policy.GetRulesInGroup(FirewallRuleNaming.Group), r => r.ServiceName == "Dnscache");
    }

    [Fact]
    public void Run_OneRuleDeletedOutsideTheApp_IsRestored()
    {
        WriteAllowList(@"C:\app\a.exe");
        Reconciler.Run();

        FirewallRuleSpec victim = _policy
            .GetRulesInGroup(FirewallRuleNaming.Group)
            .First(r => r.ServiceName == "Dnscache");
        _policy.RemoveRule(victim.Name);

        ReconcileReport report = Reconciler.Run();

        Assert.Equal(ReconcileOutcome.Reapplied, report.Outcome);
        Assert.Contains(_policy.GetRulesInGroup(FirewallRuleNaming.Group), r => r.Name == victim.Name);
    }

    [Fact]
    public void Run_AllowListUnreadable_ChangesNothing()
    {
        // Not knowing the policy is not the same as the policy being empty. Applying an empty one
        // would delete every rule the user relies on, at boot, with nobody watching.
        WriteAllowList(@"C:\app\a.exe");
        Reconciler.Run();
        int writesBefore = _policy.WriteCount;

        File.WriteAllText(StorePath, "{ corrupt");

        ReconcileReport report = Reconciler.Run();

        Assert.Equal(ReconcileOutcome.Failed, report.Outcome);
        Assert.Equal(writesBefore, _policy.WriteCount);
        Assert.NotNull(report.Error);
    }

    [Fact]
    public void Run_RevertPending_LeavesEverythingAlone()
    {
        // Somebody is part way through turning blocking on and has not confirmed. Writing rules
        // now would fight a change that may be about to be reverted wholesale.
        WriteAllowList(@"C:\app\a.exe");
        Controller.Enable(new AllowListStore(StorePath).Load(), TimeSpan.FromMinutes(10));
        int writesBefore = _policy.WriteCount;

        ReconcileReport report = Reconciler.Run();

        Assert.Equal(ReconcileOutcome.SkippedArmed, report.Outcome);
        Assert.Equal(writesBefore, _policy.WriteCount);
    }

    [Fact]
    public void Run_NeverChangesTheDefaultOutboundAction()
    {
        // Without a stored record of intent, a profile that is no longer blocking cannot be told
        // apart from one the user switched off deliberately. Guessing, at boot, is not this
        // component's decision to make.
        WriteAllowList(@"C:\app\a.exe");

        Reconciler.Run();

        Assert.DoesNotContain(_policy.Calls, c => c.StartsWith("Default:", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_NoAllowListYet_StillWritesTheBaseline()
    {
        // First boot after install. The OS services still need their rules.
        ReconcileReport report = Reconciler.Run();

        Assert.Equal(ReconcileOutcome.Reapplied, report.Outcome);
        Assert.Contains(_policy.GetRulesInGroup(FirewallRuleNaming.Group), r => r.ServiceName == "Dnscache");
    }

    [Fact]
    public void Run_LeavesRulesFromOtherProductsAlone()
    {
        _policy.Seed(new FirewallRuleSpec
        {
            Name = "Some other product rule",
            Group = "SomeOtherProduct",
            ApplicationPath = @"C:\other\tool.exe",
            Direction = FirewallDirection.Outbound,
            Action = FirewallAction.Allow,
        });

        Reconciler.Run();

        Assert.Single(_policy.GetRulesInGroup("SomeOtherProduct"));
    }

    [Fact]
    public void Report_SummaryDescribesTheOutcome()
    {
        WriteAllowList(@"C:\app\a.exe");

        // The summary is what lands in the log, and the log is the only evidence a boot-time run
        // leaves behind.
        Assert.Contains("Reapplied", Reconciler.Run().Summary, StringComparison.Ordinal);
        Assert.Contains("No drift", Reconciler.Run().Summary, StringComparison.Ordinal);
    }
}
