using System.Text.Json.Serialization;

namespace EVBlocker.Core.Baseline;

/// <summary>One Windows service that must keep outbound access when everything else is blocked.</summary>
public sealed record BaselineService
{
    /// <summary>Short service name, as <c>Get-Service -Name</c> takes it.</summary>
    public string Service { get; init; } = string.Empty;

    /// <summary>Name shown to the user.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// What breaks without it. Every entry carries one, because a baseline nobody can justify is
    /// a baseline nobody dares to trim.
    /// </summary>
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// The minimum set of OS components allowed out once default-deny is on.
/// </summary>
/// <remarks>
/// Data rather than code, so the list can be corrected on a machine that turns out to need one
/// more service without waiting for a new build.
///
/// Every entry is scoped by service name, never by executable path: these services share
/// svchost.exe, so a path-scoped rule would allow or block every service in the same process.
/// </remarks>
public sealed class BaselineDocument
{
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<BaselineService> Services { get; set; } = new();

    public const int CurrentSchemaVersion = 1;
}

/// <summary>
/// Source-generated, camelCase to match the hand-authored file. Reflection-based serialisation
/// does not survive the trimming the packaging phase applies, and fails at runtime rather than
/// at build time.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BaselineDocument))]
internal sealed partial class BaselineJsonContext : JsonSerializerContext
{
}
