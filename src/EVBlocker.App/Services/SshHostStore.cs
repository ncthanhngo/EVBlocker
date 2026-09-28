using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EVBlocker.App.Services;

/// <summary>One managed machine, as the admin listed it.</summary>
/// <remarks>
/// The name is the admin's own label - "Máy kỹ thuật 1" - so a row can be recognised without
/// memorising which IP is which. The user defaults to the current Windows account, which on a
/// company machine is usually the admin's own.
/// </remarks>
public sealed class SshHost
{
    public string DisplayName { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;
}

public sealed class SshHostList
{
    public List<SshHost> Hosts { get; set; } = new();
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SshHostList))]
internal sealed partial class SshHostJsonContext : JsonSerializerContext
{
}

/// <summary>
/// The list of machines, saved on the admin's own machine.
/// </summary>
/// <remarks>
/// LocalApplicationData, not ProgramData: this is one operator's address book, not machine
/// policy, and it has no bearing on what the firewall does. A read failure yields an empty list
/// rather than throwing - the worst case is a list the admin has to rebuild, not a broken app.
/// </remarks>
public sealed class SshHostStore
{
    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EVBlocker",
        "ssh-hosts.json");

    private readonly string _path;

    public SshHostStore() : this(DefaultPath)
    {
    }

    public SshHostStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    public SshHostList Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new SshHostList();
            }

            return JsonSerializer.Deserialize(File.ReadAllText(_path), SshHostJsonContext.Default.SshHostList)
                ?? new SshHostList();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new SshHostList();
        }
    }

    public void Save(SshHostList list)
    {
        ArgumentNullException.ThrowIfNull(list);

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(list, SshHostJsonContext.Default.SshHostList));
    }
}
