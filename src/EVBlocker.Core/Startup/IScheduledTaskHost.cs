namespace EVBlocker.Core.Startup;

/// <summary>
/// Registers, removes and tests for Windows scheduled tasks.
/// </summary>
/// <remarks>
/// Two features need scheduled tasks - the automatic revert and the boot-time reconcile - and
/// they were about to grow a second copy of the same schtasks plumbing. The task definitions
/// differ; getting one into Windows and back out again does not.
/// </remarks>
public interface IScheduledTaskHost
{
    /// <summary>True when a task with this name is registered.</summary>
    bool Exists(string taskName);

    /// <summary>Registers the task from its XML definition, replacing any task of the same name.</summary>
    void Register(string taskName, string taskXml);

    /// <summary>
    /// Removes the task. Removing one that is not there is not an error: the desired end state is
    /// the same either way.
    /// </summary>
    void Remove(string taskName);
}
