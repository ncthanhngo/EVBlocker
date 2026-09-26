using System.Globalization;
using EVBlocker.Core.Firewall;
using EVBlocker.Core.Policy;

namespace EVBlocker.Core.Startup;

public enum ReconcileOutcome
{
    /// <summary>Windows already holds exactly the rules the policy asks for.</summary>
    NoDrift,

    /// <summary>Rules had been changed or removed, and were written back.</summary>
    Reapplied,

    /// <summary>A revert is pending, so nothing was touched.</summary>
    SkippedArmed,

    /// <summary>The intended policy could not be read, so nothing was touched.</summary>
    Failed,
}

public sealed record ReconcileReport
{
    public required ReconcileOutcome Outcome { get; init; }

    public required EnforcementState State { get; init; }

    public int Added { get; init; }

    public int Removed { get; init; }

    public string? Error { get; init; }

    /// <summary>What happened to the boot guard, or null when there is none.</summary>
    public string? Guard { get; init; }

    public string Summary => Guard is null ? RulesSummary : $"{RulesSummary} Boot guard: {Guard}";

    private string RulesSummary => Outcome switch
    {
        ReconcileOutcome.NoDrift => $"No drift. Enforcement is {State}.",
        ReconcileOutcome.Reapplied => string.Create(
            CultureInfo.InvariantCulture,
            $"Reapplied policy: {Added} rule(s) written, {Removed} removed. Enforcement is {State}."),
        ReconcileOutcome.SkippedArmed =>
            "A revert is pending, so the policy was left alone until somebody confirms or it fires.",
        _ => $"Did not reconcile: {Error}",
    };
}

/// <summary>
/// Puts the firewall back in agreement with the intended policy.
/// </summary>
/// <remarks>
/// Reconciles rules only, never the default outbound action. Without a stored record of intent -
/// and there is none, deliberately - a profile that is no longer blocking is indistinguishable
/// from one the user turned off on purpose. Turning blocking back on unattended, at boot, on that
/// guess, is not a decision this should make. Leaving it alone fails towards a working network.
///
/// Rules are different: the allow-list is a stated intent, and a rule that has gone missing while
/// blocking is on means an application the user approved has quietly lost its network - or, for a
/// baseline rule, the whole machine has.
/// </remarks>
public sealed class PolicyReconciler
{
    /// <summary>How long to wait for the firewall before releasing the boot guard regardless.</summary>
    public static readonly TimeSpan FirewallWait = TimeSpan.FromMinutes(3);

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly EnforcementController _controller;
    private readonly IFirewallPolicy _policy;
    private readonly AllowListStore _store;
    private readonly IBootGuard? _bootGuard;
    private readonly Action<TimeSpan> _sleep;

    /// <param name="bootGuard">The guard to settle after reconciling. Null when there is none.</param>
    /// <param name="sleep">How to wait between polls. Replaced in tests.</param>
    public PolicyReconciler(
        EnforcementController controller,
        IFirewallPolicy policy,
        AllowListStore store,
        IBootGuard? bootGuard = null,
        Action<TimeSpan>? sleep = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(store);

        _controller = controller;
        _policy = policy;
        _store = store;
        _bootGuard = bootGuard;
        _sleep = sleep ?? Thread.Sleep;
    }

    /// <summary>
    /// Waits for the firewall, reconciles the rules, then settles the boot guard.
    /// </summary>
    /// <remarks>
    /// The guard is settled whatever happened before it, and if settling it properly fails it is
    /// released anyway. That is the one place this fails open on purpose: a guard nobody releases
    /// is a machine with no network at all, which is worse than the few seconds the guard exists
    /// to close. Every such case is written to the report, and so to the log.
    /// </remarks>
    public ReconcileReport Run()
    {
        bool ready = WaitForFirewall();
        ReconcileReport report;

        try
        {
            report = ready
                ? ReconcileRules()
                : new ReconcileReport
                {
                    Outcome = ReconcileOutcome.Failed,
                    State = EnforcementState.Off,
                    Error = string.Create(CultureInfo.InvariantCulture, $"Windows Firewall was not ready after {FirewallWait.TotalMinutes:0} minutes."),
                };
        }
#pragma warning disable CA1031 // ReconcileRules names the failures it expects; this is for the rest -
        // a COM error, a schtasks timeout - which must not stop the guard being settled below.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            report = new ReconcileReport
            {
                Outcome = ReconcileOutcome.Failed,
                State = EnforcementState.Off,
                Error = $"Unexpected: {ex.GetType().Name}: {ex.Message}",
            };
        }

        return report with { Guard = SettleBootGuard(ready) };
    }

    /// <summary>
    /// Polls until the firewall answers, which it does once MpsSvc is running and has applied
    /// its policy. Returns false on timeout.
    /// </summary>
    /// <remarks>
    /// Time is the larger of the wall clock and the sleeps: the wall clock so a poll that itself
    /// blocks cannot stretch the wait past the task's time limit - Task Scheduler would kill the
    /// run before it released the guard - and the sleeps so a test's no-op sleep still ends.
    /// </remarks>
    private bool WaitForFirewall()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();

        for (TimeSpan slept = TimeSpan.Zero; Max(slept, clock.Elapsed) < FirewallWait; slept += PollInterval)
        {
            try
            {
                _policy.GetDefaultOutboundActions();
                return true;
            }
#pragma warning disable CA1031 // Whatever the firewall throws while starting means "not yet".
            catch (Exception)
#pragma warning restore CA1031
            {
                _sleep(PollInterval);
            }
        }

        return false;
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private string? SettleBootGuard(bool firewallReady)
    {
        if (_bootGuard is null)
        {
            return null;
        }

        if (firewallReady)
        {
            try
            {
                BootGuardState? state = _controller.SyncBootGuard(prepare: false);
                return state switch
                {
                    BootGuardState.Released => "released.",
                    BootGuardState.Absent => "absent - blocking is off.",
                    _ => $"{state} after sync.",
                };
            }
#pragma warning disable CA1031 // Any failure falls through to the unconditional release below.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                return ReleaseRegardless($"sync failed ({ex.Message})");
            }
        }

        return ReleaseRegardless("firewall not ready");
    }

    private string ReleaseRegardless(string reason)
    {
        try
        {
            if (_bootGuard!.GetState() == BootGuardState.Absent)
            {
                return $"absent ({reason}).";
            }

            _bootGuard.Release();
            return $"RELEASED WITHOUT CONFIRMING THE FIREWALL POLICY - {reason}.";
        }
#pragma warning disable CA1031 // Nothing is left to try; the report is the last record.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return $"COULD NOT RELEASE - {reason}; {ex.Message}. Run: EVBlocker.exe --remove-boot-guard";
        }
    }

    private ReconcileReport ReconcileRules()
    {
        EnforcementStatus status;
        try
        {
            status = _controller.GetStatus();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
        {
            return new ReconcileReport
            {
                Outcome = ReconcileOutcome.Failed,
                State = EnforcementState.Off,
                Error = ex.Message,
            };
        }

        if (status.State == EnforcementState.Armed)
        {
            // Somebody is part way through turning blocking on and has not confirmed. Writing
            // rules now would fight a change that may be about to be reverted wholesale.
            return new ReconcileReport { Outcome = ReconcileOutcome.SkippedArmed, State = status.State };
        }

        IReadOnlyList<FirewallRuleSpec> desired;
        try
        {
            desired = _controller.BuildDesiredRules(_store.Load());
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            // Not knowing what the policy should be is not the same as the policy being empty.
            // Applying an empty one here would delete every rule the user relies on.
            return new ReconcileReport
            {
                Outcome = ReconcileOutcome.Failed,
                State = status.State,
                Error = ex.Message,
            };
        }

        var applier = new PolicyApplier(_policy);

        PolicyDiff plan;
        try
        {
            plan = applier.Plan(desired);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
        {
            return new ReconcileReport
            {
                Outcome = ReconcileOutcome.Failed,
                State = status.State,
                Error = ex.Message,
            };
        }

        if (!plan.HasChanges)
        {
            return new ReconcileReport { Outcome = ReconcileOutcome.NoDrift, State = status.State };
        }

        try
        {
            PolicyDiff applied = applier.Apply(desired);

            return new ReconcileReport
            {
                Outcome = ReconcileOutcome.Reapplied,
                State = status.State,
                Added = applied.ToAdd.Count,
                Removed = applied.ToRemove.Count,
            };
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
        {
            return new ReconcileReport
            {
                Outcome = ReconcileOutcome.Failed,
                State = status.State,
                Error = ex.Message,
            };
        }
    }
}
