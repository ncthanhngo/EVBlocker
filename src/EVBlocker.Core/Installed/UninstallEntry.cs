namespace EVBlocker.Core.Installed;

/// <summary>
/// The values of one uninstall registry key, as read.
/// </summary>
/// <remarks>
/// A plain record of what the registry held, separate from the decisions made about it. That
/// separation is what makes those decisions testable: the rules take one of these, and building
/// one needs no registry.
/// </remarks>
public sealed record UninstallEntry
{
    public string? DisplayName { get; init; }

    public string? Publisher { get; init; }

    public string? DisplayVersion { get; init; }

    public string? InstallLocation { get; init; }

    /// <summary>Usually <c>"path\to\app.exe,0"</c>, and the best single clue to the main binary.</summary>
    public string? DisplayIcon { get; init; }

    /// <summary>Written as <c>yyyyMMdd</c>, when it is written at all.</summary>
    public string? InstallDate { get; init; }

    /// <summary>Non-zero marks something Control Panel hides: drivers, plumbing, sub-packages.</summary>
    public int SystemComponent { get; init; }

    /// <summary>Set on entries that belong under another one, such as a bundled component.</summary>
    public string? ParentKeyName { get; init; }

    /// <summary>Set on updates and hotfixes, which Control Panel lists on its own page.</summary>
    public string? ReleaseType { get; init; }
}
