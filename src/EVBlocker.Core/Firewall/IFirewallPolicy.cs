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
}
