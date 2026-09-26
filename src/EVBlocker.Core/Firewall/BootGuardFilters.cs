namespace EVBlocker.Core.Firewall;

/// <summary>What a guard filter does.</summary>
public enum GuardFilterRole
{
    /// <summary>Blocks every outbound connection. The guard itself.</summary>
    Block,

    /// <summary>Lets loopback through, so local services keep talking to each other.</summary>
    Loopback,

    /// <summary>Lets DHCP through, so the machine still gets an address while the guard is up.</summary>
    Dhcp,

    /// <summary>Permits everything, outweighing the block while the machine is running.</summary>
    Release,
}

/// <summary>How long a guard filter lives.</summary>
public enum GuardFilterLifetime
{
    /// <summary>From the moment tcpip.sys loads until the Base Filtering Engine starts.</summary>
    BootTime,

    /// <summary>From the moment the Base Filtering Engine starts, and across reboots.</summary>
    Persistent,

    /// <summary>Until the Base Filtering Engine stops - in practice, until the next reboot.</summary>
    Runtime,
}

/// <summary>One filter of the boot-time outbound guard.</summary>
public sealed record GuardFilterSpec(
    Guid Key,
    string Name,
    GuardFilterRole Role,
    GuardFilterLifetime Lifetime,
    bool IPv6,
    byte Weight);

/// <summary>
/// The filters that close the outbound gap at boot: the window between the network stack loading
/// and Windows Firewall applying the default outbound action, during which Windows permits all
/// outbound traffic (measured - its own boot-time block filters cover inbound only).
/// </summary>
/// <remarks>
/// <para>
/// The block has two copies because no single lifetime covers the whole gap. A boot-time filter
/// is enforced from the moment tcpip.sys loads but is dropped the moment the Base Filtering
/// Engine starts; a persistent filter is loaded by the engine when it starts. Together there is
/// no moment between the stack coming up and the firewall applying its policy without a block,
/// and the engine's hand-over from one to the other is atomic.
/// </para>
/// <para>
/// A persistent filter is not boot-only: it is in force from the moment it is added. The release
/// is what makes the guard harmless while the machine runs - a permit of the highest weight in
/// the same sublayer, so the sublayer as a whole blocks nothing and the firewall's own filters
/// decide, exactly as before. It is a runtime filter on purpose: a reboot discards it, the block
/// takes over, and the startup task adds it back once the firewall has applied the policy.
/// </para>
/// <para>
/// The permits are soft, as WFP permits are by default, so they cannot override a block from
/// the firewall's own sublayer; they only neutralise this one.
/// </para>
/// <para>
/// Every key is fixed, so the guard can be found and removed by a process that has never seen
/// the one that installed it - including the standalone recovery script.
/// </para>
/// </remarks>
public static class BootGuardFilters
{
    public static readonly Guid ProviderKey = new("f3e821ab-76df-4bc7-b3d1-6ab40f155fad");

    public static readonly Guid SubLayerKey = new("a5977be2-6e00-42ca-9bad-22f55250fa29");

    /// <summary>
    /// Evaluated before the firewall's sublayers, so nothing they permit gets past the block
    /// while it is engaged.
    /// </summary>
    public const ushort SubLayerWeight = 0xFFFF;

    public const byte BlockWeight = 1;

    public const byte PassWeight = 8;

    public const byte ReleaseWeight = 15;

    /// <summary>The filters installed with the guard and kept until it is removed.</summary>
    public static IReadOnlyList<GuardFilterSpec> Guard { get; } =
    [
        Spec("41ffae50-5585-464c-9f0c-2a77e98ad875", GuardFilterRole.Block, GuardFilterLifetime.BootTime, false),
        Spec("8f077316-4721-42de-8bce-ba21d4cff9c8", GuardFilterRole.Block, GuardFilterLifetime.BootTime, true),
        Spec("384af285-8cee-4c10-ab1e-9ad1881b61d4", GuardFilterRole.Block, GuardFilterLifetime.Persistent, false),
        Spec("848fc931-3e92-4ccd-8144-3a98cac45983", GuardFilterRole.Block, GuardFilterLifetime.Persistent, true),

        Spec("858c1dad-a459-47c2-9a02-5e0f7798a0ff", GuardFilterRole.Loopback, GuardFilterLifetime.BootTime, false),
        Spec("44716c93-0b33-43f1-baa9-da39ebc6c05c", GuardFilterRole.Loopback, GuardFilterLifetime.BootTime, true),
        Spec("b4e6aecd-b21f-4a3c-b27a-ccd0c05ddfb9", GuardFilterRole.Loopback, GuardFilterLifetime.Persistent, false),
        Spec("7b84520e-303e-49f3-92be-e2b4303a829d", GuardFilterRole.Loopback, GuardFilterLifetime.Persistent, true),

        Spec("7fa9cc1e-6e9f-488d-9a88-55a909836366", GuardFilterRole.Dhcp, GuardFilterLifetime.BootTime, false),
        Spec("c0d67494-efc0-4efe-a95d-57c9805ff8a9", GuardFilterRole.Dhcp, GuardFilterLifetime.BootTime, true),
        Spec("49d21a39-f7c0-4975-8523-d36224cd062e", GuardFilterRole.Dhcp, GuardFilterLifetime.Persistent, false),
        Spec("70642068-44f2-4bed-83a4-c40806102da9", GuardFilterRole.Dhcp, GuardFilterLifetime.Persistent, true),
    ];

    /// <summary>The filters that neutralise the guard until the next reboot.</summary>
    public static IReadOnlyList<GuardFilterSpec> Release { get; } =
    [
        Spec("1e5da7c1-54b3-4206-9de4-ee9291b41444", GuardFilterRole.Release, GuardFilterLifetime.Runtime, false),
        Spec("ff5cb432-3817-487a-8398-c340adecf7cf", GuardFilterRole.Release, GuardFilterLifetime.Runtime, true),
    ];

    /// <summary>Every filter the guard can own, for removal.</summary>
    public static IEnumerable<GuardFilterSpec> All => Release.Concat(Guard);

    /// <summary>DHCP server port: the destination of a client's request.</summary>
    public static ushort DhcpServerPort(bool ipv6) => ipv6 ? (ushort)547 : (ushort)67;

    private static GuardFilterSpec Spec(string key, GuardFilterRole role, GuardFilterLifetime lifetime, bool ipv6)
    {
        byte weight = role switch
        {
            GuardFilterRole.Block => BlockWeight,
            GuardFilterRole.Release => ReleaseWeight,
            _ => PassWeight,
        };

        string name = $"EVBlocker boot guard - {role} - {lifetime} - {(ipv6 ? "IPv6" : "IPv4")}";
        return new GuardFilterSpec(new Guid(key), name, role, lifetime, ipv6, weight);
    }
}
