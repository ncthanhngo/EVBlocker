using System.Text.Json.Serialization;

namespace EVBlocker.Core.Policy;

/// <summary>One executable the user has allowed out to the internet.</summary>
public sealed record AllowedApp
{
    /// <summary>Full path of the executable, as the firewall rule will scope it.</summary>
    public string ExecutablePath { get; init; } = string.Empty;

    /// <summary>What the UI shows. Defaults to the file name when the user does not rename it.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Hex SHA-256 of the executable when it was added, or null when it could not be read.
    /// </summary>
    /// <remarks>
    /// Recorded to notice that an allowed executable has been replaced since - an update, or
    /// something worse. It is a signal to show the user, never grounds for the app to silently
    /// revoke a decision the user made.
    /// </remarks>
    public string? Sha256 { get; init; }

    public DateTimeOffset AddedAt { get; init; }
}

/// <summary>
/// The file on disk. Wraps the list so the format can gain fields later without the root of the
/// document changing shape.
/// </summary>
public sealed class AllowListDocument
{
    /// <summary>
    /// Bumped when the shape changes in a way a previous version could not read. Present from
    /// version one: adding a version field to a format that shipped without one means guessing
    /// what the unversioned files were.
    /// </summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<AllowedApp> Apps { get; set; } = new();

    public const int CurrentSchemaVersion = 1;
}

/// <summary>
/// Source-generated serialisation. Reflection-based serialisation does not survive the trimming
/// the packaging phase applies, and it fails at runtime rather than at build time.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AllowListDocument))]
internal sealed partial class AllowListJsonContext : JsonSerializerContext
{
}
