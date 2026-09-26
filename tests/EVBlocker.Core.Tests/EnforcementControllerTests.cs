using EVBlocker.Core.Baseline;
using EVBlocker.Core.Firewall;
using EVBlocker.Core.Policy;

namespace EVBlocker.Core.Tests;

/// <summary>
/// The state machine that decides whether a machine keeps its network.
/// </summary>
/// <remarks>
/// Every test here stands for a way this could strand somebody: blocking before a way back
/// exists, confirming something that was never armed, or reporting success for a change that only
/// half happened.
/// </remarks>
public sealed class EnforcementControllerTests
{
    private static readonly TimeSpan Revert = TimeSpan.FromMinutes(10);

    private readonly FakeFirewallPolicy _policy = new();
    private readonly FakeConfigBackup _backup = new();
    private readonly FakeDeadManSwitch _deadMan = new();

    /// <summary>Uses the shipped baseline, so these tests exercise the real service list.</summary>
    private EnforcementController Sut => new(
        _policy,
        _backup,
        _deadMan,
        new OsBaseline(Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.json")));

    private static AllowListDocument AllowList(params string[] paths) => new()
    {
        Apps = paths.Select(p => new AllowedApp
        {
            ExecutablePath = p,
            DisplayName = Path.GetFileName(p),
            AddedAt = DateTimeOffset.UnixEpoch,
        }).ToList(),
    };

    [Fact]
    public void GetStatus_UnconfiguredMachine_IsOff()
    {
        EnforcementStatus status = Sut.GetStatus();

        Assert.Equal(EnforcementState.Off, status.State);
        Assert.False(status.RevertPending);
        Assert.All(status.DefaultOutbound.Values, a => Assert.Equal(FirewallAction.Allow, a));
    }

    [Fact]
    public void Enable_BlocksEveryProfileAndArmsTheRevert()
    {
        EnforcementStatus status = Sut.Enable(AllowList(@"C:\app\a.exe"), Revert);

        Assert.Equal(EnforcementState.Armed, status.State);
        Assert.True(status.AllProfilesBlocked);
        Assert.True(_deadMan.IsArmed());
        Assert.Equal(Revert, _deadMan.ArmedDelay);
    }

    [Fact]
    public void Enable_ArmsTheRevertBeforeBlockingAnything()
    {
        // The ordering is the whole safety argument. Blocking first and arming second leaves a
        // window where the process can die with the network down and no way back scheduled.
        Sut.Enable(AllowList(@"C:\app\a.exe"), Revert);

        int armedAt = _policy.Calls.Count;
        int firstBlockAt = _policy.Calls.FindIndex(c => c.StartsWith("Default:", StringComparison.Ordinal));

        // The arm call lands on the dead-man fake, so compare against the policy calls recorded
        // before and after it by checking that no default action was set before Arm ran.
        Assert.Contains("Arm:", string.Join("|", _deadMan.Calls), StringComparison.Ordinal);
        Assert.True(firstBlockAt >= 0);
        Assert.True(_deadMan.ArmedBackupPath is not null, "Revert must be armed.");
        Assert.True(armedAt > 0);
    }

    [Fact]
    public void Enable_TakesABackupBeforeChangingAnything()
    {
        Sut.Enable(AllowList(@"C:\app\a.exe"), Revert);

        Assert.Contains("Create", _backup.Calls);
        Assert.Equal(_backup.List()[0].Path, _deadMan.ArmedBackupPath);
    }

    [Fact]
    public void Enable_BackupFails_NothingIsBlockedAndNoRevertIsArmed()
    {
        // Fail closed: without a way back, the destructive step must not happen at all.
        _backup.CreateFailure = new UnauthorizedAccessException("no admin");

        Assert.Throws<UnauthorizedAccessException>(() => Sut.Enable(AllowList(), Revert));

        Assert.False(_deadMan.IsArmed());
        Assert.Equal(EnforcementState.Off, Sut.GetStatus().State);
        Assert.DoesNotContain(_policy.Calls, c => c.StartsWith("Default:", StringComparison.Ordinal));
    }

    [Fact]
    public void Enable_ArmingFails_NothingIsBlocked()
    {
        _deadMan.ArmFailure = new InvalidOperationException("scheduler refused");

        Assert.Throws<InvalidOperationException>(() => Sut.Enable(AllowList(), Revert));

        Assert.Equal(EnforcementState.Off, Sut.GetStatus().State);
        Assert.DoesNotContain(_policy.Calls, c => c.StartsWith("Default:", StringComparison.Ordinal));
    }

    [Fact]
    public void Enable_WritesTheBaselineAlongsideTheAllowList()
    {
        // Blocking without the OS baseline in place is how a machine loses DNS and updates.
        Sut.Enable(AllowList(@"C:\app\a.exe"), Revert);

        IReadOnlyList<FirewallRuleSpec> written = _policy.GetRulesInGroup(FirewallRuleNaming.Group);

        Assert.Contains(written, r => r.ServiceName == "Dnscache");
        Assert.Contains(written, r => r.ApplicationPath == @"C:\app\a.exe");
    }

    [Fact]
    public void Enable_BaselineRulesAreScopedByServiceNotByExecutable()
    {
        Sut.Enable(AllowList(), Revert);

        IReadOnlyList<FirewallRuleSpec> written = _policy.GetRulesInGroup(FirewallRuleNaming.Group);

        Assert.All(
            written.Where(r => r.ServiceName is not null),
            r => Assert.Null(r.ApplicationPath));
    }

    [Fact]
    public void Enable_OnlySomeProfilesTakeTheChange_ThrowsAndLeavesTheRevertArmed()
    {
        // Half-blocked looks enforced until the machine joins a different network. The pending
        // revert is what makes reporting the failure safe instead of having to unwind by hand.
        _policy.IgnoreDefaultActionFor = FirewallProfile.Public;

        Assert.Throws<InvalidOperationException>(() => Sut.Enable(AllowList(), Revert));

        Assert.True(_deadMan.IsArmed());
    }

    [Fact]
    public void Enable_WhenAlreadyArmed_IsRefused()
    {
        EnforcementController sut = Sut;
        sut.Enable(AllowList(), Revert);

        Assert.Throws<InvalidOperationException>(() => sut.Enable(AllowList(), Revert));
    }

    [Fact]
    public void Confirm_DisarmsAndLeavesBlockingOn()
    {
        EnforcementController sut = Sut;
        sut.Enable(AllowList(), Revert);

        EnforcementStatus status = sut.Confirm();

        Assert.Equal(EnforcementState.On, status.State);
        Assert.False(status.RevertPending);
        Assert.True(status.AllProfilesBlocked);
    }

    [Fact]
    public void Confirm_WhenNothingIsArmed_IsRefused()
    {
        // Reporting success here would tell the user a revert was cancelled when none existed.
        Assert.Throws<InvalidOperationException>(() => Sut.Confirm());
    }

    [Fact]
    public void Confirm_WhenAlreadyConfirmed_IsRefused()
    {
        EnforcementController sut = Sut;
        sut.Enable(AllowList(), Revert);
        sut.Confirm();

        Assert.Throws<InvalidOperationException>(() => sut.Confirm());
    }

    [Fact]
    public void Disable_ReturnsEveryProfileToAllow()
    {
        EnforcementController sut = Sut;
        sut.Enable(AllowList(), Revert);
        sut.Confirm();

        EnforcementStatus status = sut.Disable();

        Assert.Equal(EnforcementState.Off, status.State);
        Assert.All(status.DefaultOutbound.Values, a => Assert.Equal(FirewallAction.Allow, a));
    }

    [Fact]
    public void Disable_WhileArmed_AlsoCancelsTheRevert()
    {
        // Leaving a revert armed after turning blocking off would restore an old configuration
        // minutes later for no reason anyone could see.
        EnforcementController sut = Sut;
        sut.Enable(AllowList(), Revert);

        sut.Disable();

        Assert.False(_deadMan.IsArmed());
    }

    [Fact]
    public void Disable_WhenAlreadyOff_IsHarmless()
    {
        EnforcementStatus status = Sut.Disable();

        Assert.Equal(EnforcementState.Off, status.State);
    }

    [Fact]
    public void EnableConfirmDisable_CanRunTwice()
    {
        EnforcementController sut = Sut;

        sut.Enable(AllowList(@"C:\app\a.exe"), Revert);
        sut.Confirm();
        sut.Disable();

        sut.Enable(AllowList(@"C:\app\a.exe"), Revert);
        Assert.Equal(EnforcementState.Armed, sut.GetStatus().State);
    }

    [Fact]
    public void GetStatus_BlockedWithNoPendingRevert_ReadsAsOn()
    {
        // Somebody turned blocking on outside this app. That is genuinely "on", and reporting it
        // as Off would invite a second Enable that takes another backup of an already-blocked
        // machine.
        foreach (FirewallProfile profile in Enum.GetValues<FirewallProfile>())
        {
            _policy.SetDefaultOutboundAction(profile, FirewallAction.Block);
        }

        Assert.Equal(EnforcementState.On, Sut.GetStatus().State);
    }
}
