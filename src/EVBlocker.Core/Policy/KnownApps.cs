using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EVBlocker.Core.Policy;

/// <summary>An application the catalogue knows where to look for.</summary>
public sealed record KnownApp
{
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Places to look, in order of preference. May contain environment variables and one
    /// <c>*</c> per path segment.
    /// </summary>
    public List<string> Candidates { get; init; } = new();
}

public sealed class KnownAppsDocument
{
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<KnownApp> Apps { get; set; } = new();

    public const int CurrentSchemaVersion = 1;
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(KnownAppsDocument))]
internal sealed partial class KnownAppsJsonContext : JsonSerializerContext
{
}

/// <summary>One application found on this machine.</summary>
public sealed record DiscoveredApp(string Name, string ExecutablePath);

/// <summary>
/// Finds common applications so the allow-list does not have to be built one file dialog at a time.
/// </summary>
/// <remarks>
/// A catalogue plus a search, not a list of fixed paths. The same application lives in different
/// places on different machines, some install under a directory named for their version, and any
/// given machine has only some of them. Writing fixed paths would create rules pointing at files
/// that are not there - which Windows accepts, and which then silently allow nothing.
///
/// Only applications that actually exist are returned. Nothing is added without the caller asking.
/// </remarks>
public sealed class KnownApps
{
    private const string EmbeddedResourceName = "EVBlocker.Core.Policy.known-apps.json";

    private readonly string? _overridePath;

    public KnownApps(string? overridePath = null) => _overridePath = overridePath ?? OverridePath;

    /// <summary>A file here replaces the shipped catalogue, for machines with other software.</summary>
    public static string OverridePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "EVBlocker",
        "known-apps.json");

    public bool UsingOverride => _overridePath is not null && File.Exists(_overridePath);

    /// <exception cref="InvalidDataException">The catalogue cannot be parsed.</exception>
    public KnownAppsDocument Load()
    {
        string json = UsingOverride ? File.ReadAllText(_overridePath!) : ReadEmbedded();
        string source = UsingOverride ? _overridePath! : "the built-in catalogue";

        KnownAppsDocument? document;
        try
        {
            document = JsonSerializer.Deserialize(json, KnownAppsJsonContext.Default.KnownAppsDocument);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Known-apps catalogue in {source} is not valid JSON.", ex);
        }

        if (document is null)
        {
            throw new InvalidDataException($"Known-apps catalogue in {source} is empty.");
        }

        if (document.SchemaVersion > KnownAppsDocument.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Known-apps catalogue in {source} has schema version {document.SchemaVersion}, "
                + $"but this build understands at most {KnownAppsDocument.CurrentSchemaVersion}.");
        }

        return document;
    }

    /// <summary>
    /// The catalogued applications present on this machine, one entry per application.
    /// </summary>
    /// <remarks>
    /// The first candidate that matches wins, so the catalogue's ordering decides which install
    /// is preferred when an application is present in more than one place.
    /// </remarks>
    public IReadOnlyList<DiscoveredApp> Discover()
    {
        var found = new List<DiscoveredApp>();

        foreach (KnownApp app in Load().Apps)
        {
            string? path = app.Candidates
                .SelectMany(PathGlob.ExpandFiles)
                .FirstOrDefault();

            if (path is not null)
            {
                found.Add(new DiscoveredApp(app.Name, path));
            }
        }

        return found;
    }

    private static string ReadEmbedded()
    {
        using Stream? stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(EmbeddedResourceName);

        if (stream is null)
        {
            throw new InvalidDataException(
                $"Embedded catalogue '{EmbeddedResourceName}' is missing from the assembly.");
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
