using EVBlocker.Core.Baseline;
using EVBlocker.Core.Policy;
using EVBlocker.Core.Safety;

namespace EVBlocker.Core.Firewall;

public enum EnforcementState
{
    /// <summary>Unmatched outbound traffic is allowed. Windows' own default.</summary>
    Off,

    /// <summary>Blocking is on and an automatic revert is pending. Nobody has confirmed yet.</summary>
    Armed,

    /// <summary>Blocking is on and confirmed. No revert is waiting.</summary>
    On,
}

public sealed record EnforcementStatus
{
    public required EnforcementState State { get; init; }

    public required IReadOnlyDictionary<FirewallProfile, FirewallAction> DefaultOutbound { get; init; }

    /// <summary>Whether an automatic revert is scheduled.</summary>
    public required bool RevertPending { get; init; }

    /// <summary>
    /// False when only some profiles are blocked, which means a previous change did not finish.
    /// Worth showing, because a machine blocked on Public but not Private looks enforced until
    /// somebody joins a different network.
    /// </summary>
    public bool AllProfilesBlocked => DefaultOutbound.Values.All(a => a == FirewallAction.Block);
}

/// <summary>
/// Turns outbound default-deny on and off, and refuses to do it unsafely.
/// </summary>
/// <remarks>
/// The state is derived from the machine rather than stored in a file. Blocking lives in the
/// firewall configuration and a pending revert lives in the task scheduler; both outlive this
/// process, and both can be changed by someone else. A state file would be a second opinion that
/// can disagree with either, and the moment it disagrees is the moment somebody needs the truth.
///
/// Enable does the destructive step last, on purpose: the backup is taken, the rules are written
/// and the automatic revert is armed before anything is blocked. If the process dies at any point
/// after arming, the revert still fires; if it dies before, nothing has been blocked yet.
/// </remarks>
public sealed class EnforcementController
{
    private readonly IFirewallPolicy _policy;
    private readonly IConfigBackup _backup;
    private readonly IDeadManSwitch _deadMan;
    private readonly OsBaseline _baseline;

    public EnforcementController(
        IFirewallPolicy policy,
        IConfigBackup backup,
        IDeadManSwitch deadMan,
        OsBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(backup);
        ArgumentNullException.ThrowIfNull(deadMan);
        ArgumentNullException.ThrowIfNull(baseline);

        _policy = policy;
        _backup = backup;
        _deadMan = deadMan;
        _baseline = baseline;
    }

    public EnforcementStatus GetStatus()
    {
        IReadOnlyDictionary<FirewallProfile, FirewallAction> defaults = _policy.GetDefaultOutboundActions();
        bool blocking = defaults.Values.Any(a => a == FirewallAction.Block);
        bool revertPending = _deadMan.IsArmed();

        return new EnforcementStatus
        {
            State = blocking
                ? revertPending ? EnforcementState.Armed : EnforcementState.On
                : EnforcementState.Off,
            DefaultOutbound = defaults,
            RevertPending = revertPending,
        };
    }

    /// <summary>
    /// The rules that must exist before outbound traffic can be blocked: the OS baseline plus
    /// everything the user has allowed.
    /// </summary>
    /// <remarks>
    /// Combined into one list because they have to be reconciled together. Applying them in two
    /// passes would make each pass treat the other's rules as unwanted and delete them.
    /// </remarks>
    public IReadOnlyList<FirewallRuleSpec> BuildDesiredRules(AllowListDocument allowList)
    {
        ArgumentNullException.ThrowIfNull(allowList);

        var desired = new List<FirewallRuleSpec>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (FirewallRuleSpec rule in OsBaseline.BuildRules(_baseline.Load())
                     .Concat(PolicyApplier.BuildDesired(allowList)))
        {
            // Service rules and application rules are named differently so a clash should be
            // impossible; dropping duplicates anyway keeps one name from mapping to two rules.
            if (names.Add(rule.Name))
            {
                desired.Add(rule);
            }
        }

        return desired;
    }

    /// <summary>
    /// Applies the baseline and allow-list, arms the automatic revert, then blocks outbound
    /// traffic that no rule matches.
    /// </summary>
    /// <param name="revertAfter">
    /// How long the user has to confirm. After this the machine returns to the backed-up
    /// configuration on its own.
    /// </param>
    /// <exception cref="InvalidOperationException">Enforcement is already on.</exception>
    /// <exception cref="UnauthorizedAccessException">Not running elevated.</exception>
    public EnforcementStatus Enable(AllowListDocument allowList, TimeSpan revertAfter)
    {
        ArgumentNullException.ThrowIfNull(allowList);

        // No elevation check here. Every privileged step below already demands it and says which
        // operation failed, and a static check against the real process would make this whole
        // state machine untestable without an administrator - which is exactly the code that
        // most needs testing.
        EnforcementStatus before = GetStatus();
        if (before.State != EnforcementState.Off)
        {
            throw new InvalidOperationException(
                $"Outbound blocking is already {before.State}. Confirm it or turn it off first.");
        }

        // Loaded first so a broken baseline stops everything before a single change is made.
        // A baseline that fails to load after blocking would be a machine with no network.
        IReadOnlyList<FirewallRuleSpec> desired = BuildDesiredRules(allowList);

        // Second, because everything after this point needs something to fall back to.
        BackupInfo backup = _backup.Create();

        new PolicyApplier(_policy).Apply(desired);

        // Armed before blocking, never after. Between these two calls the machine is still
        // reachable; after them it may not be, and by then the way back already exists.
        _deadMan.Arm(backup.Path, revertAfter);

        foreach (FirewallProfile profile in Enum.GetValues<FirewallProfile>())
        {
            _policy.SetDefaultOutboundAction(profile, FirewallAction.Block);
        }

        EnforcementStatus after = GetStatus();

        if (!after.AllProfilesBlocked)
        {
            // The pending revert will undo the partial change on its own, so this reports the
            // failure rather than trying to unwind it here.
            throw new InvalidOperationException(
                "Outbound blocking did not take effect on every profile. "
                + "The scheduled revert will restore the previous configuration.");
        }

        return after;
    }

    /// <summary>
    /// Cancels the automatic revert, leaving blocking in place.
    /// </summary>
    /// <remarks>
    /// Only meaningful while armed. Calling it when nothing is armed would quietly report success
    /// for a confirmation that confirmed nothing.
    /// </remarks>
    public EnforcementStatus Confirm()
    {
        EnforcementStatus status = GetStatus();
        if (status.State != EnforcementState.Armed)
        {
            throw new InvalidOperationException(
                $"Nothing to confirm: outbound blocking is {status.State}.");
        }

        _deadMan.Disarm();
        return GetStatus();
    }

    /// <summary>
    /// Returns unmatched outbound traffic to Allow and cancels any pending revert.
    /// </summary>
    /// <remarks>
    /// Surgical rather than a restore from backup: it changes the one setting that matters and
    /// leaves every rule alone, where importing a backup would also discard rules other software
    /// added since. Use <see cref="IConfigBackup.Restore"/> when the whole configuration is wrong.
    ///
    /// This sets an explicit Allow, which is not the same as the state most machines start in.
    /// An untouched profile reads as NotConfigured in Get-NetFirewallProfile and falls back to
    /// Windows' built-in behaviour, which is to allow. The COM enum has no NotConfigured value -
    /// it reports the effective action - so there is no way to put a profile back to unset
    /// through this API. The behaviour is identical on a standalone machine; on one where Group
    /// Policy owns the setting, an explicit local value is a change worth knowing about, and
    /// restoring the backup is the way to undo it exactly.
    ///
    /// The allow rules this app created stay behind. They do nothing while the default is Allow,
    /// and keeping them means turning enforcement back on does not have to rebuild them.
    /// </remarks>
    public EnforcementStatus Disable()
    {
        foreach (FirewallProfile profile in Enum.GetValues<FirewallProfile>())
        {
            _policy.SetDefaultOutboundAction(profile, FirewallAction.Allow);
        }

        if (_deadMan.IsArmed())
        {
            _deadMan.Disarm();
        }

        return GetStatus();
    }
}
