using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EVBlocker.Core.Firewall;

/// <summary>
/// Builds the display name of a rule this app owns.
/// </summary>
/// <remarks>
/// Two properties matter, and both are why this is not just string concatenation at the call site.
///
/// Deterministic: the same executable must produce the same name every time, or re-applying the
/// policy would add a second rule instead of recognising the existing one.
///
/// Unique: Windows removes a rule by name, so two different executables sharing a name would make
/// removal ambiguous. File names collide constantly - every Electron app ships an updater called
/// update.exe - so the name carries a short digest of the full path.
/// </remarks>
public static class FirewallRuleNaming
{
    /// <summary>Grouping value on every rule this app creates.</summary>
    public const string Group = "EVBlocker";

    /// <summary>
    /// Digest characters kept. Eight hex characters is 32 bits: ample against accidental
    /// collision across the few hundred rules a machine might hold, and short enough that the
    /// name still reads as a name in wf.msc.
    /// </summary>
    private const int DigestLength = 8;

    public static string ForApplication(string executablePath, FirewallAction action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        string fileName = Path.GetFileName(executablePath);
        if (string.IsNullOrEmpty(fileName))
        {
            fileName = executablePath;
        }

        // Lower-cased like the digest is. Windows paths are case-insensitive, so the same
        // executable reached as A.EXE and as a.exe has to yield one name - otherwise the applier
        // sees two rules where there is one program, and removal by name picks one at random.
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Group} - {Verb(action)} - {fileName.ToLowerInvariant()} [{Digest(executablePath)}]");
    }

    public static string ForService(string serviceName, FirewallAction action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Group} - {Verb(action)} - service:{serviceName}");
    }

    private static string Verb(FirewallAction action) => action == FirewallAction.Allow ? "Allow" : "Block";

    /// <summary>
    /// Case-insensitive on purpose: Windows paths are, so the same executable reached through a
    /// differently-cased path has to produce the same rule rather than a duplicate.
    /// </summary>
    private static string Digest(string path)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
        return Convert.ToHexString(hash)[..DigestLength];
    }
}
