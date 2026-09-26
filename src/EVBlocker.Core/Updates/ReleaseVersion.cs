using System.Globalization;

namespace EVBlocker.Core.Updates;

/// <summary>
/// Reads the version out of a GitHub release tag.
/// </summary>
/// <remarks>
/// Lives in Core rather than in the UI because it is the one part of update checking that has
/// real branching and no network: a tag is written by hand at release time, so it arrives in
/// whatever shape somebody typed - with or without a leading v, with or without a suffix, and
/// occasionally as something that is not a version at all. Getting that wrong either hides a real
/// update or nags about one that does not exist.
/// </remarks>
public static class ReleaseVersion
{
    /// <summary>
    /// Parses tags such as <c>v1.2.3</c>, <c>1.2</c> or <c>v2.0.0-beta.1</c>.
    /// </summary>
    /// <remarks>
    /// Any suffix after the numbers is dropped. Whether a release is a prerelease is reported by
    /// the GitHub API as its own field, which is more reliable than inferring it from the tag.
    /// </remarks>
    public static bool TryParse(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);

        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        ReadOnlySpan<char> span = tag.Trim();

        if (span.Length > 0 && (span[0] == 'v' || span[0] == 'V'))
        {
            span = span[1..];
        }

        // Keep only the leading numeric part: "1.2.3-beta.1" becomes "1.2.3", and a build suffix
        // is not something to compare against.
        int end = 0;
        while (end < span.Length && (char.IsAsciiDigit(span[end]) || span[end] == '.'))
        {
            end++;
        }

        span = span[..end].TrimEnd('.');

        if (span.IsEmpty)
        {
            return false;
        }

        // Version.TryParse rejects a bare "2", which is a perfectly ordinary tag.
        if (!span.Contains('.'))
        {
            if (int.TryParse(span, NumberStyles.Integer, CultureInfo.InvariantCulture, out int major))
            {
                version = new Version(major, 0, 0);
                return true;
            }

            return false;
        }

        if (!Version.TryParse(span, out Version? parsed))
        {
            return false;
        }

        // Normalised so comparisons are not thrown by unset components, which Version reports
        // as -1 and which order below 0.
        version = new Version(
            Math.Max(parsed.Major, 0),
            Math.Max(parsed.Minor, 0),
            Math.Max(parsed.Build, 0));

        return true;
    }

    /// <summary>True when <paramref name="tag"/> names a version above <paramref name="current"/>.</summary>
    public static bool IsNewerThan(string? tag, Version current)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (!TryParse(tag, out Version candidate))
        {
            // An unreadable tag is not grounds for telling somebody an update exists.
            return false;
        }

        var normalisedCurrent = new Version(
            Math.Max(current.Major, 0),
            Math.Max(current.Minor, 0),
            Math.Max(current.Build, 0));

        return candidate > normalisedCurrent;
    }
}
