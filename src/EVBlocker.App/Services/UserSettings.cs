using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using EVBlocker.App.Theming;

namespace EVBlocker.App.Services;

/// <summary>Preferences that belong to the person, not to the machine.</summary>
/// <remarks>
/// Stored under LocalApplicationData, unlike the allow-list and the baseline, which live in
/// ProgramData because they describe machine policy. Which theme somebody likes is not policy,
/// and two people sharing a machine should not have to agree about it.
/// </remarks>
public sealed class UserSettings
{
    /// <summary>
    /// Stored by name, not as the enum.
    /// </summary>
    /// <remarks>
    /// System.Text.Json writes an enum as its numeric value, so reordering AppTheme would
    /// silently reinterpret every settings file already on disk and flip people's theme. A name
    /// also survives an unknown value: anything unrecognised reads back as the default.
    /// </remarks>
    public string Theme { get; set; } = nameof(AppTheme.Light);

    public AppTheme GetTheme() =>
        Enum.TryParse(Theme, ignoreCase: true, out AppTheme parsed) ? parsed : AppTheme.Light;

    public void SetTheme(AppTheme theme) => Theme = theme.ToString();
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(UserSettings))]
internal sealed partial class UserSettingsJsonContext : JsonSerializerContext
{
}

internal static class UserSettingsStore
{
    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EVBlocker",
        "settings.json");

    /// <summary>
    /// Reads the saved preferences, or the defaults.
    /// </summary>
    /// <remarks>
    /// Any failure yields defaults rather than propagating. Unlike the allow-list, where an
    /// unreadable file must stop everything, the worst case here is the wrong theme.
    /// </remarks>
    public static UserSettings Load()
    {
        try
        {
            if (!File.Exists(Path))
            {
                return new UserSettings();
            }

            return JsonSerializer.Deserialize(
                File.ReadAllText(Path),
                UserSettingsJsonContext.Default.UserSettings) ?? new UserSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new UserSettings();
        }
    }

    public static void Save(UserSettings settings)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(
                Path,
                JsonSerializer.Serialize(settings, UserSettingsJsonContext.Default.UserSettings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A preference that fails to persist is a preference that resets next launch, which
            // is not worth interrupting anybody over.
        }
    }
}
