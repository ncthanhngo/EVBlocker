namespace EVBlocker.Core.Firewall;

public enum BootGuardState
{
    /// <summary>No guard filter exists.</summary>
    Absent,

    /// <summary>The guard is installed and released: it will block at the next boot, not now.</summary>
    Released,

    /// <summary>The guard is installed and blocking all outbound traffic right now.</summary>
    Engaged,

    /// <summary>Some guard filters exist and some do not - an install or removal did not finish.</summary>
    Partial,
}

/// <summary>
/// The boot-time outbound guard: blocks outbound traffic from the moment the network stack loads
/// until Windows Firewall has applied its policy. See <see cref="BootGuardFilters"/>.
/// </summary>
public interface IBootGuard
{
    BootGuardState GetState();

    /// <summary>
    /// Installs the guard, released. Nothing changes until the next boot. Idempotent.
    /// </summary>
    void Install();

    /// <summary>
    /// Adds the release, ending the block until the next reboot. Idempotent.
    /// </summary>
    void Release();

    /// <summary>
    /// Removes the release, so the guard blocks now as it does at boot. Cuts the machine off; used
    /// only to verify that the guard works.
    /// </summary>
    void Engage();

    /// <summary>Removes every guard filter, the sublayer and the provider. Idempotent.</summary>
    void Remove();
}
