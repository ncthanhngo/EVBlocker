using System.Text.Json;

namespace EVBlocker.Core.Policy;

/// <summary>
/// Reads and writes the user's allow-list.
/// </summary>
/// <remarks>
/// Stored under ProgramData, not AppData: the policy applies to the machine, and a per-user copy
/// would mean the firewall state and the file describing it could disagree depending on who
/// logged in.
///
/// The path is a constructor argument so the store can be tested against a temporary directory;
/// <see cref="DefaultPath"/> is what the app passes.
/// </remarks>
public sealed class AllowListStore
{
    private readonly string _path;

    public AllowListStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "EVBlocker",
        "allowlist.json");

    /// <summary>
    /// The stored list, or an empty one when the file does not exist yet.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The file exists but cannot be parsed, or records a schema this build does not understand.
    /// Deliberately not swallowed into an empty list: silently treating an unreadable allow-list
    /// as "nothing is allowed" would revoke every decision the user has made.
    /// </exception>
    public AllowListDocument Load()
    {
        if (!File.Exists(_path))
        {
            return new AllowListDocument();
        }

        string json = File.ReadAllText(_path);

        AllowListDocument? document;
        try
        {
            document = JsonSerializer.Deserialize(json, AllowListJsonContext.Default.AllowListDocument);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Allow-list at '{_path}' is not valid JSON.", ex);
        }

        if (document is null)
        {
            throw new InvalidDataException($"Allow-list at '{_path}' is empty.");
        }

        if (document.SchemaVersion > AllowListDocument.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Allow-list at '{_path}' has schema version {document.SchemaVersion}, "
                + $"but this build understands at most {AllowListDocument.CurrentSchemaVersion}. "
                + "It was probably written by a newer version of EVBlocker.");
        }

        return document;
    }

    /// <summary>
    /// Writes the list, replacing any previous content.
    /// </summary>
    /// <remarks>
    /// Written to a temporary file and then moved into place. A direct write that is interrupted -
    /// power loss, a killed process - leaves a truncated file, and a truncated allow-list is one
    /// the next Load rejects, which is a worse outcome than losing the most recent change.
    /// </remarks>
    public void Save(AllowListDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.SchemaVersion = AllowListDocument.CurrentSchemaVersion;

        string directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);

        // Alongside the target, not in the temp directory: File.Move is only atomic within one
        // volume, and ProgramData and TEMP are not guaranteed to be on the same one.
        string temporary = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(document, AllowListJsonContext.Default.AllowListDocument));

            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                TryDelete(temporary);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A stray temp file is harmless and must not mask whatever actually went wrong.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
