using System.Text.RegularExpressions;
using System.Xml.Linq;
using EVBlocker.Core.Baseline;
using EVBlocker.Core.Firewall;
using EVBlocker.Core.Policy;
using EVBlocker.Core.Safety;
using EVBlocker.Core.Startup;

namespace EVBlocker.Core.Tests;

/// <summary>
/// How the boot guard follows blocking on and off, and how the startup run releases it.
/// </summary>
/// <remarks>
/// Each failure here is a machine that either leaks at boot or has no network after a reboot.
/// </remarks>
public sealed class BootGuardLifecycleTests : IDisposable
{
    private static readonly TimeSpan Revert = TimeSpan.FromMinutes(10);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"evblocker-guard-{Guid.NewGuid():N}");
    private readonly FakeFirewallPolicy _policy = new();
    private readonly FakeConfigBackup _backup = new();
    private readonly FakeDeadManSwitch _deadMan = new();
    private readonly FakeBootGuard _guard;
    private readonly List<TimeSpan> _sleeps = new();

    public BootGuardLifecycleTests() => _guard = new FakeBootGuard(_policy.Calls);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private int _prepared;

    private EnforcementController Controller => new(
        _policy,
        _backup,
        _deadMan,
        new OsBaseline(Path.Combine(_directory, "no-baseline-override.json")),
        _guard,
        () =>
        {
            _prepared++;
            _policy.Calls.Add("Prepare");
        });

    private PolicyReconciler Reconciler => new(
        Controller,
        _policy,
        new AllowListStore(Path.Combine(_directory, "allowlist.json")),
        _guard,
        _sleeps.Add);

    private static AllowListDocument AllowList() => new()
    {
        Apps = [new AllowedApp { ExecutablePath = @"C:\app\a.exe", DisplayName = "a.exe", AddedAt = DateTimeOffset.UnixEpoch }],
    };

    private int IndexOf(string call) => _policy.Calls.IndexOf(call);

    private int FirstBlock => _policy.Calls.FindIndex(c => c.StartsWith("Default:", StringComparison.Ordinal) && c.EndsWith("=Block", StringComparison.Ordinal));

    [Fact]
    public void Enable_PreparesTheReleaseThenInstallsTheGuard_BeforeBlocking()
    {
        // A guard installed before anything can release it would strand the next boot; one
        // installed after blocking leaves the first reboot unguarded if the process dies between.
        Controller.Enable(AllowList(), Revert);

        Assert.True(IndexOf("Prepare") >= 0);
        Assert.True(IndexOf("Prepare") < IndexOf("Guard:Install"));
        Assert.True(IndexOf("Guard:Install") < FirstBlock);
        Assert.Equal(BootGuardState.Released, _guard.State);
    }

    [Fact]
    public void Enable_GuardFails_NothingIsBlocked()
    {
        _guard.InstallFailure = new InvalidOperationException("WFP said no");

        Assert.Throws<InvalidOperationException>(() => Controller.Enable(AllowList(), Revert));

        Assert.Equal(-1, FirstBlock);
        Assert.True(_deadMan.IsArmed(), "The revert stays armed and will undo the rules.");
    }

    [Fact]
    public void Enable_PreparationFails_NoGuardAndNothingBlocked()
    {
        var controller = new EnforcementController(
            _policy, _backup, _deadMan,
            new OsBaseline(Path.Combine(_directory, "none.json")),
            _guard,
            () => throw new InvalidOperationException("Program Files is locked"));

        Assert.Throws<InvalidOperationException>(() => controller.Enable(AllowList(), Revert));

        Assert.Equal(-1, IndexOf("Guard:Install"));
        Assert.Equal(-1, FirstBlock);
    }

    [Fact]
    public void Disable_RemovesTheGuard_AfterAllowing()
    {
        Controller.Enable(AllowList(), Revert);

        Controller.Disable();

        int lastAllow = _policy.Calls.FindLastIndex(c => c.EndsWith("=Allow", StringComparison.Ordinal));
        Assert.True(lastAllow >= 0 && lastAllow < IndexOf("Guard:Remove"));
        Assert.Equal(BootGuardState.Absent, _guard.State);
    }

    [Fact]
    public void Sync_BlockingWithoutGuard_InstallsIt()
    {
        // A machine where blocking was turned on before the guard existed - this one included.
        _policy.BlockAll();

        BootGuardState? state = Controller.SyncBootGuard(prepare: true);

        Assert.Equal(BootGuardState.Released, state);
        Assert.Equal(1, _prepared);
    }

    [Fact]
    public void Sync_NotBlocking_RemovesALeftoverGuard()
    {
        // A dead-man revert restores Allow through netsh, which cannot touch WFP filters.
        _guard.State = BootGuardState.Engaged;

        BootGuardState? state = Controller.SyncBootGuard(prepare: true);

        Assert.Equal(BootGuardState.Absent, state);
        Assert.Equal(0, _prepared);
    }

    [Fact]
    public void Sync_AlreadyReleased_StillPreparesTheRelease()
    {
        // A released guard whose startup task was deleted outside the app: skipping the
        // preparation here would leave the next boot with nothing to release the guard.
        _policy.BlockAll();
        _guard.State = BootGuardState.Released;

        Controller.SyncBootGuard(prepare: true);

        Assert.Equal(1, _prepared);
        Assert.Equal(-1, IndexOf("Guard:Install"));
    }

    [Fact]
    public void Sync_WithoutPrepare_DoesNotTouchTheStartupTask()
    {
        // The startup task passes false: re-registering itself mid-run is not its job.
        _policy.BlockAll();
        _guard.State = BootGuardState.Engaged;

        Controller.SyncBootGuard(prepare: false);

        Assert.Equal(0, _prepared);
        Assert.Equal(BootGuardState.Released, _guard.State);
    }

    [Fact]
    public void Controller_WithoutGuard_BehavesAsBefore()
    {
        var controller = new EnforcementController(_policy, _backup, _deadMan, new OsBaseline(Path.Combine(_directory, "none.json")));

        controller.Enable(AllowList(), Revert);

        Assert.Null(controller.SyncBootGuard(prepare: true));
        Assert.DoesNotContain(_policy.Calls, c => c.StartsWith("Guard:", StringComparison.Ordinal));
    }

    [Fact]
    public void Boot_ReleasesAnEngagedGuard_AfterReconcilingRules()
    {
        _policy.BlockAll();
        _guard.State = BootGuardState.Engaged;

        ReconcileReport report = Reconciler.Run();

        Assert.Equal(BootGuardState.Released, _guard.State);
        Assert.True(_policy.Calls.FindLastIndex(c => c.StartsWith("Add:", StringComparison.Ordinal)) < IndexOf("Guard:Install"));
        Assert.Contains("released", report.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Boot_WaitsForTheFirewall_ThenReleases()
    {
        _policy.BlockAll();
        _guard.State = BootGuardState.Engaged;
        _policy.FailingDefaultReads = 5;

        Reconciler.Run();

        Assert.Equal(5, _sleeps.Count);
        Assert.Equal(BootGuardState.Released, _guard.State);
    }

    [Fact]
    public void Boot_FirewallNeverReady_ReleasesAnyway_AndSaysSo()
    {
        // Fails open on purpose: a guard nobody releases is a machine with no network at all.
        _guard.State = BootGuardState.Engaged;
        _policy.FailingDefaultReads = int.MaxValue;

        ReconcileReport report = Reconciler.Run();

        Assert.Equal(BootGuardState.Released, _guard.State);
        Assert.Equal(ReconcileOutcome.Failed, report.Outcome);
        Assert.Contains("RELEASED WITHOUT CONFIRMING", report.Summary, StringComparison.Ordinal);
        Assert.True(_sleeps.Aggregate(TimeSpan.Zero, (a, b) => a + b) >= PolicyReconciler.FirewallWait - TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Boot_SyncThrows_ReleasesAnyway()
    {
        _policy.BlockAll();
        _guard.State = BootGuardState.Engaged;
        _guard.InstallFailure = new InvalidOperationException("sublayer vanished");

        ReconcileReport report = Reconciler.Run();

        Assert.Equal(BootGuardState.Released, _guard.State);
        Assert.Contains("sublayer vanished", report.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Boot_EvenWhenTheAllowListIsUnreadable_TheGuardIsReleased()
    {
        _policy.BlockAll();
        _guard.State = BootGuardState.Engaged;
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "allowlist.json"), "{ not json");

        ReconcileReport report = Reconciler.Run();

        Assert.Equal(ReconcileOutcome.Failed, report.Outcome);
        Assert.Equal(BootGuardState.Released, _guard.State);
    }

    [Theory]
    [InlineData("com")]
    [InlineData("timeout")]
    [InlineData("argument")]
    public void Boot_UnexpectedErrorWhileReconciling_StillReleasesTheGuard(string kind)
    {
        // Errors outside the ones ReconcileRules names - a COM failure, a schtasks timeout -
        // must not skip the release.
        _policy.BlockAll();
        _guard.State = BootGuardState.Engaged;
        _policy.RuleReadFailure = kind switch
        {
            "com" => new System.Runtime.InteropServices.COMException("RPC server unavailable", unchecked((int)0x800706BA)),
            "timeout" => new TimeoutException("schtasks did not answer"),
            _ => new ArgumentException("bad rule"),
        };

        ReconcileReport report = Reconciler.Run();

        Assert.Equal(ReconcileOutcome.Failed, report.Outcome);
        Assert.Equal(BootGuardState.Released, _guard.State);
        Assert.Contains("Unexpected", report.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Boot_BlockingOff_RemovesTheGuard()
    {
        _guard.State = BootGuardState.Engaged;

        Reconciler.Run();

        Assert.Equal(BootGuardState.Absent, _guard.State);
    }

    [Fact]
    public void DeadMan_RemovesTheGuard_AfterRestoringTheFirewall()
    {
        const string exe = @"C:\Program Files\EVBlocker\EVBlocker.exe";
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        XElement[] actions = XDocument.Parse(ScheduledTaskDeadManSwitch.BuildTaskXml(@"C:\b.wfw", DateTimeOffset.Now.AddMinutes(5), exe))
            .Descendants(ns + "Exec").ToArray();

        Assert.Equal(2, actions.Length);
        Assert.EndsWith("netsh.exe", actions[0].Element(ns + "Command")!.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(exe, actions[1].Element(ns + "Command")!.Value);
        Assert.Equal(ScheduledTaskDeadManSwitch.RemoveBootGuardSwitch, actions[1].Element(ns + "Arguments")!.Value);
    }

    [Fact]
    public void DeadMan_WithoutGuard_HasOnlyTheImport()
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        Assert.Single(XDocument.Parse(ScheduledTaskDeadManSwitch.BuildTaskXml(@"C:\b.wfw", DateTimeOffset.Now.AddMinutes(5)))
            .Descendants(ns + "Exec"));
    }

    [Fact]
    public void RecoveryScript_KnowsEveryKeyTheGuardUses()
    {
        // The script is the way back when the executable is gone. A key it does not know is a
        // filter it leaves behind - and if that filter is the block, the machine stays offline.
        string script = File.ReadAllText(Path.Combine(RepoRoot(), "tools", "remove-boot-guard.ps1"));

        HashSet<Guid> inScript = Regex.Matches(script, "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")
            .Select(m => Guid.Parse(m.Value))
            .ToHashSet();

        IEnumerable<Guid> expected = BootGuardFilters.All.Select(f => f.Key)
            .Append(BootGuardFilters.SubLayerKey)
            .Append(BootGuardFilters.ProviderKey);

        Assert.All(expected, key => Assert.Contains(key, inScript));
    }

    private static string RepoRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "EVBlocker.sln")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Repository root not found above the test output.");
    }
}
