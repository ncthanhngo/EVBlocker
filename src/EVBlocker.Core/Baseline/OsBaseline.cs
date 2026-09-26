using System.Reflection;
using System.Text.Json;
using EVBlocker.Core.Firewall;

namespace EVBlocker.Core.Baseline;

/// <summary>
/// Loads the OS baseline and turns it into firewall rules.
/// </summary>
/// <remarks>
/// The shipped list is an embedded resource, but a file at <see cref="OverridePath"/> wins if it
/// exists. A machine that turns out to need one more service can be fixed on the spot instead of
/// waiting for a release, which matters because the failure mode of a short baseline is a machine
/// with no working network.
///
/// This baseline is additive. Windows' own "Core Networking" rules stay enabled and already cover
/// DHCP, DNS over the wire, IPv6 and ICMP; re-deriving them here would mean maintaining a worse
/// copy of a list Microsoft keeps current.
/// </remarks>
public sealed class OsBaseline
{
    private const string EmbeddedResourceName = "EVBlocker.Core.Baseline.baseline-allow.json";

    private readonly string? _overridePath;

    public OsBaseline(string? overridePath = null) => _overridePath = overridePath ?? OverridePath;

    public static string OverridePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "EVBlocker",
        "baseline-allow.json");

    /// <summary>True when an operator has placed a file that replaces the shipped baseline.</summary>
    public bool UsingOverride => _overridePath is not null && File.Exists(_overridePath);

    /// <summary>
    /// Reads and validates the baseline.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The document is unparseable, records a schema this build does not understand, or contains
    /// an entry that would not produce a correctly scoped rule.
    /// </exception>
    public BaselineDocument Load()
    {
        string json = UsingOverride ? File.ReadAllText(_overridePath!) : ReadEmbedded();
        string source = UsingOverride ? _overridePath! : "the built-in baseline";

        BaselineDocument? document;
        try
        {
            document = JsonSerializer.Deserialize(json, BaselineJsonContext.Default.BaselineDocument);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Baseline in {source} is not valid JSON.", ex);
        }

        if (document is null)
        {
            throw new InvalidDataException($"Baseline in {source} is empty.");
        }

        Validate(document, source);
        return document;
    }

    /// <summary>
    /// One outbound Allow per service.
    /// </summary>
    /// <remarks>
    /// Scoped by ServiceName and nothing else. Adding an executable path would pin these to
    /// svchost.exe, and a path-scoped rule on svchost allows every service sharing that process -
    /// which is the whole failure this baseline exists to avoid.
    /// </remarks>
    public static IReadOnlyList<FirewallRuleSpec> BuildRules(BaselineDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document.Services
            .Select(service => new FirewallRuleSpec
            {
                Name = FirewallRuleNaming.ForService(service.Service, FirewallAction.Allow),
                Group = FirewallRuleNaming.Group,
                ServiceName = service.Service,
                ApplicationPath = null,
                Direction = FirewallDirection.Outbound,
                Action = FirewallAction.Allow,
                Profiles = FirewallProfiles.All,
                Enabled = true,
                Description = $"EVBlocker baseline: {service.DisplayName} — {service.Reason}",
            })
            .ToList();
    }

    private static void Validate(BaselineDocument document, string source)
    {
        if (document.SchemaVersion > BaselineDocument.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Baseline in {source} has schema version {document.SchemaVersion}, "
                + $"but this build understands at most {BaselineDocument.CurrentSchemaVersion}.");
        }

        if (document.Services.Count == 0)
        {
            throw new InvalidDataException(
                $"Baseline in {source} lists no services. An empty baseline would leave the "
                + "machine with no working network once outbound traffic is blocked.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (BaselineService service in document.Services)
        {
            if (string.IsNullOrWhiteSpace(service.Service))
            {
                throw new InvalidDataException(
                    $"Baseline in {source} has an entry with no service name. A rule with no "
                    + "scope allows everything, which is the opposite of a baseline.");
            }

            // A wildcard would widen the rule past the one service the entry claims to be about.
            if (service.Service.Contains('*', StringComparison.Ordinal)
                || service.Service.Contains('?', StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Baseline in {source} has a wildcard service name '{service.Service}'.");
            }

            if (!seen.Add(service.Service))
            {
                throw new InvalidDataException(
                    $"Baseline in {source} lists service '{service.Service}' more than once. "
                    + "Two rules would share one name, and removal by name could only delete one.");
            }

            if (string.IsNullOrWhiteSpace(service.Reason))
            {
                throw new InvalidDataException(
                    $"Baseline in {source} has no reason for '{service.Service}'. "
                    + "An entry nobody can justify is one nobody dares to remove.");
            }
        }
    }

    private static string ReadEmbedded()
    {
        using Stream? stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(EmbeddedResourceName);

        if (stream is null)
        {
            throw new InvalidDataException(
                $"Embedded baseline '{EmbeddedResourceName}' is missing from the assembly.");
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
