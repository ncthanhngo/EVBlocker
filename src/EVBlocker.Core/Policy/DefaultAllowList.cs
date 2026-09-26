namespace EVBlocker.Core.Policy;

/// <summary>
/// Works out which catalogued applications are missing from an allow-list.
/// </summary>
/// <remarks>
/// Separated from the view model so the rule that decides whether a default belongs in the list
/// can be tested. The act of adding needs a file on disk; the decision does not.
/// </remarks>
public static class DefaultAllowList
{
    /// <summary>
    /// The discovered applications that are neither in <paramref name="document"/> already nor
    /// recorded there as removed.
    /// </summary>
    /// <remarks>
    /// Compared by executable path. An application that updates into a directory named for its
    /// new version therefore counts as a different file, and a default removed before such an
    /// update comes back once. Matching by name instead would collide between the catalogue and
    /// the file names manual entries carry.
    /// </remarks>
    public static IReadOnlyList<DiscoveredApp> MissingFrom(
        AllowListDocument document,
        IReadOnlyList<DiscoveredApp> discovered)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(discovered);

        HashSet<string> settled = document.Apps
            .Select(app => app.ExecutablePath)
            .Concat(document.RemovedDefaults)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return discovered.Where(app => !settled.Contains(app.ExecutablePath)).ToList();
    }
}
