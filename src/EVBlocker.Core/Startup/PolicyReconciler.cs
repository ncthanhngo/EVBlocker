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

    public string Summary => Outcome switch
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
    private readonly EnforcementController _controller;
    private readonly IFirewallPolicy _policy;
    private readonly AllowListStore _store;

    public PolicyReconciler(EnforcementController controller, IFirewallPolicy policy, AllowListStore store)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(store);

        _controller = controller;
        _policy = policy;
        _store = store;
    }

    public ReconcileReport Run()
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
