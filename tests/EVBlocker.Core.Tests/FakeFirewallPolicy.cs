using EVBlocker.Core.Firewall;
using EVBlocker.Core.Safety;

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

    /// <summary>Starts where an unconfigured Windows machine starts: outbound is allowed.</summary>
    private readonly Dictionary<FirewallProfile, FirewallAction> _defaults =
        Enum.GetValues<FirewallProfile>().ToDictionary(p => p, _ => FirewallAction.Allow);

    public List<string> Calls { get; } = new();

    /// <summary>
    /// Makes SetDefaultOutboundAction quietly do nothing for this profile, standing in for a
    /// machine where the change does not take on every profile.
    /// </summary>
    public FirewallProfile? IgnoreDefaultActionFor { get; set; }

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

    public IReadOnlyDictionary<FirewallProfile, FirewallAction> GetDefaultOutboundActions() =>
        new Dictionary<FirewallProfile, FirewallAction>(_defaults);

    public void SetDefaultOutboundAction(FirewallProfile profile, FirewallAction action)
    {
        Calls.Add($"Default:{profile}={action}");

        if (IgnoreDefaultActionFor == profile)
        {
            return;
        }

        _defaults[profile] = action;
    }
}

/// <summary>In-memory backup store. Create records a path without writing anything.</summary>
internal sealed class FakeConfigBackup : IConfigBackup
{
    private readonly List<BackupInfo> _backups = new();

    public List<string> Calls { get; } = new();

    /// <summary>Set to make Create fail, standing in for a machine where export is refused.</summary>
    public Exception? CreateFailure { get; set; }

    public BackupInfo Create()
    {
        Calls.Add("Create");

        if (CreateFailure is not null)
        {
            throw CreateFailure;
        }

        var info = new BackupInfo(
            $@"C:\ProgramData\EVBlocker\backups\firewall-{_backups.Count:00}.wfw",
            DateTimeOffset.Now);

        _backups.Insert(0, info);
        return info;
    }

    public IReadOnlyList<BackupInfo> List() => _backups;

    public void Restore(string path) => Calls.Add($"Restore:{path}");
}

/// <summary>In-memory dead-man switch, recording what it was armed with.</summary>
internal sealed class FakeDeadManSwitch : IDeadManSwitch
{
    public List<string> Calls { get; } = new();

    public string? ArmedBackupPath { get; private set; }

    public TimeSpan? ArmedDelay { get; private set; }

    public Exception? ArmFailure { get; set; }

    public bool IsArmed() => ArmedBackupPath is not null;

    public void Arm(string backupPath, TimeSpan delay)
    {
        Calls.Add($"Arm:{backupPath}");

        if (ArmFailure is not null)
        {
            throw ArmFailure;
        }

        ArmedBackupPath = backupPath;
        ArmedDelay = delay;
    }

    public void Disarm()
    {
        Calls.Add("Disarm");
        ArmedBackupPath = null;
        ArmedDelay = null;
    }
}
