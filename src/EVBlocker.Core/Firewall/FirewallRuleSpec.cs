namespace EVBlocker.Core.Firewall;

/// <summary>Matches NET_FW_RULE_DIRECTION.</summary>
public enum FirewallDirection
{
    Inbound = 1,
    Outbound = 2,
}

/// <summary>Matches NET_FW_ACTION.</summary>
public enum FirewallAction
{
    Block = 0,
    Allow = 1,
}

/// <summary>
/// A single network profile. Separate from <see cref="FirewallProfiles"/> on purpose: a rule
/// applies to a set of profiles, whereas a default action belongs to exactly one, and the API
/// that reads a default action cannot answer for a combination. Two types make the difference
/// impossible to get wrong by passing the flags value.
/// </summary>
public enum FirewallProfile
{
    Domain = 1,
    Private = 2,
    Public = 4,
}

/// <summary>Matches NET_FW_PROFILE_TYPE2.</summary>
[Flags]
public enum FirewallProfiles
{
    Domain = 1,
    Private = 2,
    Public = 4,

    /// <summary>
    /// The value Windows itself uses for "all profiles". Not Domain|Private|Public: the firewall
    /// stores the sentinel, and a rule written with 7 reads back as three named profiles, which
    /// would make every comparison against a freshly read rule report a difference.
    /// </summary>
    All = 0x7FFFFFFF,
}

/// <summary>
/// One Windows Firewall rule, in the shape this app cares about.
/// </summary>
/// <remarks>
/// A record so two rules compare by value: the whole reconcile step is "is what Windows has the
/// same as what we want", and that is only cheap if equality is structural.
///
/// The fields Windows supports but this app never sets - ports, addresses, ICMP types, edge
/// traversal - are deliberately absent. A field that is always null is a field someone will
/// eventually set by accident.
/// </remarks>
public sealed record FirewallRuleSpec
{
    /// <summary>
    /// Display name, and the key Windows removes a rule by. Must be unique and deterministic,
    /// which is what <see cref="FirewallRuleNaming"/> exists to guarantee.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Grouping value. Every rule this app owns carries the same one, which is what makes
    /// "delete everything we created" a safe operation rather than a guess.
    /// </summary>
    public required string Group { get; init; }

    /// <summary>Full path of the executable the rule applies to, or null for a service rule.</summary>
    public string? ApplicationPath { get; init; }

    /// <summary>
    /// Short service name, for the OS components that share svchost.exe. Scoping those by
    /// executable path would allow or block every service hosted in the same process.
    /// </summary>
    public string? ServiceName { get; init; }

    public required FirewallDirection Direction { get; init; }

    public required FirewallAction Action { get; init; }

    public FirewallProfiles Profiles { get; init; } = FirewallProfiles.All;

    public bool Enabled { get; init; } = true;

    public string? Description { get; init; }
}
