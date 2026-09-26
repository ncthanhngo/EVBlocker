using EVBlocker.Core.Firewall;

namespace EVBlocker.Core.Policy;

/// <summary>What applying the allow-list would change, or did change.</summary>
public sealed record PolicyDiff
{
    public required IReadOnlyList<FirewallRuleSpec> ToAdd { get; init; }

    /// <summary>
    /// Rules in this app's group that the allow-list no longer asks for. Only ever rules from
    /// that group - nothing else on the machine is a candidate for removal.
    /// </summary>
    public required IReadOnlyList<FirewallRuleSpec> ToRemove { get; init; }

    /// <summary>
    /// Rules already correct. Reported rather than discarded so a caller can tell "nothing to do"
    /// apart from "nothing is configured".
    /// </summary>
    public required IReadOnlyList<FirewallRuleSpec> Unchanged { get; init; }

    public bool HasChanges => ToAdd.Count > 0 || ToRemove.Count > 0;
}

/// <summary>
/// Turns the allow-list into firewall rules, and reconciles what Windows has with what the
/// allow-list asks for.
/// </summary>
/// <remarks>
/// Written against <see cref="IFirewallPolicy"/> rather than COM, so every branch here is unit
/// tested against a fake. The diffing is where mistakes would be expensive and invisible - a
/// rule quietly not applied leaves an app blocked with no error anywhere.
/// </remarks>
public sealed class PolicyApplier
{
    private readonly IFirewallPolicy _policy;

    public PolicyApplier(IFirewallPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _policy = policy;
    }

    /// <summary>
    /// The rules the allow-list asks for. One outbound Allow per entry.
    /// </summary>
    /// <remarks>
    /// Entries with a blank path are skipped rather than turned into a rule with no scope, which
    /// Windows would read as "allow everything".
    /// </remarks>
    public static IReadOnlyList<FirewallRuleSpec> BuildDesired(AllowListDocument allowList)
    {
        ArgumentNullException.ThrowIfNull(allowList);

        var desired = new List<FirewallRuleSpec>(allowList.Apps.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (AllowedApp app in allowList.Apps)
        {
            if (string.IsNullOrWhiteSpace(app.ExecutablePath))
            {
                continue;
            }

            string name = FirewallRuleNaming.ForApplication(app.ExecutablePath, FirewallAction.Allow);

            // The same executable listed twice would otherwise produce two rules with one name,
            // and removal by name could then only ever delete one of them.
            if (!seen.Add(name))
            {
                continue;
            }

            desired.Add(new FirewallRuleSpec
            {
                Name = name,
                Group = FirewallRuleNaming.Group,
                ApplicationPath = app.ExecutablePath,
                Direction = FirewallDirection.Outbound,
                Action = FirewallAction.Allow,
                Profiles = FirewallProfiles.All,
                Enabled = true,
                Description = string.IsNullOrWhiteSpace(app.DisplayName)
                    ? "Allowed by EVBlocker"
                    : $"Allowed by EVBlocker: {app.DisplayName}",
            });
        }

        return desired;
    }

    /// <summary>Works out what would change, without touching the firewall.</summary>
    public PolicyDiff Plan(AllowListDocument allowList) => Plan(BuildDesired(allowList));

    /// <summary>
    /// Overload taking the rules directly, so a caller can reconcile against more than the
    /// allow-list. Enforcement needs the OS baseline applied alongside it, and the two have to be
    /// diffed together: planning them separately would have each view the other's rules as
    /// unwanted and delete them.
    /// </summary>
    public PolicyDiff Plan(IReadOnlyList<FirewallRuleSpec> desired)
    {
        ArgumentNullException.ThrowIfNull(desired);

        IReadOnlyList<FirewallRuleSpec> actual = _policy.GetRulesInGroup(FirewallRuleNaming.Group);

        var actualByName = new Dictionary<string, FirewallRuleSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (FirewallRuleSpec rule in actual)
        {
            // A duplicate name means the firewall already holds two rules this app cannot tell
            // apart. Keep the first and let the second fall through to removal, which is how the
            // duplicate gets cleaned up rather than preserved forever.
            actualByName.TryAdd(rule.Name, rule);
        }

        var toAdd = new List<FirewallRuleSpec>();
        var unchanged = new List<FirewallRuleSpec>();
        var desiredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (FirewallRuleSpec rule in desired)
        {
            desiredNames.Add(rule.Name);

            if (actualByName.TryGetValue(rule.Name, out FirewallRuleSpec? existing)
                && Equivalent(rule, existing))
            {
                unchanged.Add(rule);
            }
            else
            {
                // Covers both "absent" and "present but drifted": adding under an existing name
                // replaces the rule, so one operation handles either case.
                toAdd.Add(rule);
            }
        }

        var toRemove = actual
            .Where(rule => !desiredNames.Contains(rule.Name))
            .ToList();

        return new PolicyDiff { ToAdd = toAdd, ToRemove = toRemove, Unchanged = unchanged };
    }

    /// <summary>
    /// Applies the allow-list and reports what was done.
    /// </summary>
    /// <remarks>
    /// Removals run before additions so a rename frees its old name first, and so a run that
    /// fails part way leaves fewer allowances than intended rather than more.
    /// </remarks>
    public PolicyDiff Apply(AllowListDocument allowList) => Apply(BuildDesired(allowList));

    /// <summary>Overload taking the rules directly; see the note on the matching Plan overload.</summary>
    public PolicyDiff Apply(IReadOnlyList<FirewallRuleSpec> desired)
    {
        PolicyDiff diff = Plan(desired);

        foreach (FirewallRuleSpec rule in diff.ToRemove)
        {
            // Defence in depth. GetRulesInGroup already filters, but a removal is irreversible
            // and the cost of being wrong is deleting somebody else's firewall rule.
            if (!string.Equals(rule.Group, FirewallRuleNaming.Group, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Refusing to remove rule '{rule.Name}': it belongs to group '{rule.Group}', "
                    + $"not '{FirewallRuleNaming.Group}'.");
            }

            _policy.RemoveRule(rule.Name);
        }

        foreach (FirewallRuleSpec rule in diff.ToAdd)
        {
            _policy.AddRule(rule);
        }

        return diff;
    }

    /// <summary>
    /// Compares only the fields this app sets, and compares paths case-insensitively because
    /// Windows paths are. Plain record equality would report a difference every run for a rule
    /// Windows had merely stored with different casing, and the applier would rewrite it forever.
    /// </summary>
    private static bool Equivalent(FirewallRuleSpec desired, FirewallRuleSpec actual) =>
        string.Equals(desired.ApplicationPath, actual.ApplicationPath, StringComparison.OrdinalIgnoreCase)
        && string.Equals(desired.ServiceName, actual.ServiceName, StringComparison.OrdinalIgnoreCase)
        && desired.Direction == actual.Direction
        && desired.Action == actual.Action
        && desired.Profiles == actual.Profiles
        && desired.Enabled == actual.Enabled;
}
