using EVBlocker.Core.Firewall;

namespace EVBlocker.Core.Tests;

/// <summary>
/// In-memory stand-in for Windows Firewall.
/// </summary>
/// <remarks>
/// Records the calls as well as the state, because several of the behaviours that matter are
/// about what was called and in what order - that removals precede additions, and that a run
/// with nothing to do writes nothing at all.
/// </remarks>
internal sealed class FakeFirewallPolicy : IFirewallPolicy
{
    private readonly Dictionary<string, FirewallRuleSpec> _rules = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Calls { get; } = new();

    public int WriteCount => Calls.Count(c => c.StartsWith("Add:", StringComparison.Ordinal)
                                              || c.StartsWith("Remove:", StringComparison.Ordinal));

    public void Seed(params FirewallRuleSpec[] rules)
    {
        foreach (FirewallRuleSpec rule in rules)
        {
            _rules[rule.Name] = rule;
        }
    }

    public IReadOnlyList<FirewallRuleSpec> GetRulesInGroup(string group)
    {
        Calls.Add($"Get:{group}");

        return _rules.Values
            .Where(r => string.Equals(r.Group, group, StringComparison.Ordinal))
            .ToList();
    }

    public void AddRule(FirewallRuleSpec rule)
    {
        Calls.Add($"Add:{rule.Name}");
        _rules[rule.Name] = rule;
    }

    public void RemoveRule(string name)
    {
        Calls.Add($"Remove:{name}");
        _rules.Remove(name);
    }
}
