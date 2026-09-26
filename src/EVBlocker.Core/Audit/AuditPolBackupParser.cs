using System.Globalization;

namespace EVBlocker.Core.Audit;

/// <summary>
/// Parses the CSV that <c>auditpol /backup</c> writes.
/// </summary>
/// <remarks>
/// Why the CSV rather than <c>auditpol /get</c>: the /get output states the setting as words
/// ("Failure", "Success and Failure", "No Auditing") and those words are localised, so parsing
/// them breaks on any non-English Windows. The backup CSV carries a numeric "Setting Value"
/// column whose meaning is locale-independent.
///
/// Pure so it can be unit tested against sample CSV, with no elevation and no auditpol run.
/// </remarks>
internal static class AuditPolBackupParser
{
    // Fallback column positions, used when the header cannot be matched by name because the
    // header text itself is localised. This layout has been stable across Windows releases.
    private const int FallbackGuidColumn = 3;
    private const int FallbackSettingColumn = 6;

    private const string GuidHeader = "Subcategory GUID";
    private const string SettingHeader = "Setting Value";

    /// <summary>
    /// The setting recorded for <paramref name="subcategoryGuid"/>, or null when the CSV holds no
    /// row for it (the subcategory is absent from the backup when it has never been configured).
    /// </summary>
    public static AuditSetting? FindSetting(string csv, string subcategoryGuid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subcategoryGuid);

        if (string.IsNullOrWhiteSpace(csv))
        {
            return null;
        }

        string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
        {
            return null;
        }

        string[] header = SplitRow(lines[0]);
        int guidColumn = IndexOfHeader(header, GuidHeader, FallbackGuidColumn);
        int settingColumn = IndexOfHeader(header, SettingHeader, FallbackSettingColumn);

        foreach (string line in lines.Skip(1))
        {
            string[] fields = SplitRow(line);

            if (fields.Length <= Math.Max(guidColumn, settingColumn))
            {
                continue;
            }

            if (!fields[guidColumn].Equals(subcategoryGuid, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (int.TryParse(
                    fields[settingColumn],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int value))
            {
                // Mask off anything outside the two flags this code understands, so an unexpected
                // bit cannot turn into a nonsense enum value.
                return (AuditSetting)(value & (int)(AuditSetting.Success | AuditSetting.Failure));
            }
        }

        return null;
    }

    private static int IndexOfHeader(string[] header, string name, int fallback)
    {
        for (int i = 0; i < header.Length; i++)
        {
            if (header[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return fallback;
    }

    // Fields in this CSV are plain: no embedded commas or quotes in machine names, subcategory
    // names or GUIDs. A full CSV reader would be dead weight here.
    private static string[] SplitRow(string line)
    {
        string[] fields = line.TrimEnd('\r').Split(',');

        for (int i = 0; i < fields.Length; i++)
        {
            fields[i] = fields[i].Trim();
        }

        return fields;
    }
}
