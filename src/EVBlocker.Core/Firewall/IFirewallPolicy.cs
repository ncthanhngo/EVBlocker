namespace EVBlocker.Core.Firewall;

/// <summary>
/// The Windows Firewall operations this app needs.
/// </summary>
/// <remarks>
/// Narrow on purpose. Everything above this line is ordinary testable code working against value
/// types; everything below it is COM. Keeping the seam this small means the part that cannot be
/// unit tested is also the part with no branching in it.
///
/// Reads work for a standard user. Writes require elevation, and the implementation is expected
/// to surface that as a clear failure rather than a COM error code.
/// </remarks>
public interface IFirewallPolicy
{
    /// <summary>
    /// Every rule carrying <paramref name="group"/> as its Grouping value.
    /// Rules outside the group are not returned, and must never be touched.
    /// </summary>
    IReadOnlyList<FirewallRuleSpec> GetRulesInGroup(string group);

    /// <summary>Adds a rule. Adding a name that already exists replaces the existing rule.</summary>
    void AddRule(FirewallRuleSpec rule);

    /// <summary>
    /// Removes the rule with this display name. Removing a name that is not present is not an
    /// error: the desired end state is the same either way.
    /// </summary>
    void RemoveRule(string name);

    /// <summary>
    /// What happens to outbound traffic that no rule matches, per profile.
    /// </summary>
    /// <remarks>
    /// Windows allows unmatched outbound traffic by default, so this reads Allow on a machine
    /// nobody has configured. Blocking it is what turns an allow-list from a list of exceptions
    /// into the policy, and is the single most destructive setting this app touches.
    ///
    /// This reports the effective action, not how it was configured. A profile nobody has touched
    /// shows as NotConfigured in Get-NetFirewallProfile and reads as Allow here, because the COM
    /// enum has only Block and Allow. Verified against a machine in that state.
    /// </remarks>
    IReadOnlyDictionary<FirewallProfile, FirewallAction> GetDefaultOutboundActions();

    /// <summary>Sets the default outbound action for one profile.</summary>
    void SetDefaultOutboundAction(FirewallProfile profile, FirewallAction action);
}
