namespace EVBlocker.Core.Safety;

/// <summary>
/// A revert that happens on its own unless somebody stops it.
/// </summary>
/// <remarks>
/// The point is surviving this process. A timer inside the app dies with the app, and the moment
/// it is most needed is precisely the moment the app has crashed, been killed, or the machine has
/// been rebooted with a broken network policy in place.
/// </remarks>
public interface IDeadManSwitch
{
    /// <summary>Whether a pending revert exists right now.</summary>
    bool IsArmed();

    /// <summary>
    /// Schedules a restore of <paramref name="backupPath"/> after <paramref name="delay"/>,
    /// replacing any revert already scheduled.
    /// </summary>
    void Arm(string backupPath, TimeSpan delay);

    /// <summary>Cancels the pending revert. Doing this when nothing is armed is not an error.</summary>
    void Disarm();
}
