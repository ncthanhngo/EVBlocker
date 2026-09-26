namespace EVBlocker.Core.Policy;

/// <summary>
/// Expands an executable path pattern into the files that actually exist.
/// </summary>
/// <remarks>
/// Needed because several applications install under a directory named for their version -
/// JetBrains IDEs and Google Drive both do - so no fixed path finds them. Environment variables
/// are expanded too, since the same application lives under Program Files on one machine and
/// under the user's profile on another.
///
/// Deliberately not a general glob: one <c>*</c> per segment, matched against directory and file
/// names only. Anything more would be a matching engine nobody asked for.
/// </remarks>
public static class PathGlob
{
    /// <summary>
    /// Files matching <paramref name="pattern"/>, newest first where a wildcard matched several.
    /// </summary>
    /// <remarks>
    /// Newest first because a version-named directory pattern usually matches several installs
    /// and the most recent one is the one in use.
    /// </remarks>
    public static IReadOnlyList<string> ExpandFiles(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return Array.Empty<string>();
        }

        string expanded = Environment.ExpandEnvironmentVariables(pattern);

        // An unexpanded variable means the machine has no such location; matching literally
        // against a path containing "%ProgramFiles%" would only ever find nothing.
        if (expanded.Contains('%', StringComparison.Ordinal))
        {
            return Array.Empty<string>();
        }

        if (!expanded.Contains('*', StringComparison.Ordinal))
        {
            return File.Exists(expanded) ? new[] { expanded } : Array.Empty<string>();
        }

        string[] segments = expanded.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Length < 2)
        {
            return Array.Empty<string>();
        }

        // The first segment is the drive or share; it never contains a wildcard.
        var directories = new List<string> { segments[0] + Path.DirectorySeparatorChar };

        for (int i = 1; i < segments.Length - 1; i++)
        {
            directories = Descend(directories, segments[i]);

            if (directories.Count == 0)
            {
                return Array.Empty<string>();
            }
        }

        return directories
            .SelectMany(directory => SafeEnumerateFiles(directory, segments[^1]))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();
    }

    private static List<string> Descend(List<string> directories, string segment)
    {
        var next = new List<string>();

        foreach (string directory in directories)
        {
            if (!segment.Contains('*', StringComparison.Ordinal))
            {
                string candidate = Path.Combine(directory, segment);
                if (Directory.Exists(candidate))
                {
                    next.Add(candidate);
                }

                continue;
            }

            next.AddRange(SafeEnumerateDirectories(directory, segment));
        }

        return next;
    }

    // Enumeration throws on directories the current user cannot read, which is ordinary when
    // walking Program Files. A directory that cannot be read simply contributes no matches.
    private static IEnumerable<string> SafeEnumerateDirectories(string directory, string pattern)
    {
        try
        {
            return Directory.EnumerateDirectories(directory, pattern).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string directory, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(directory, pattern).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
