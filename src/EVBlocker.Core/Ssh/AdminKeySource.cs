using System.IO;

namespace EVBlocker.Core.Ssh;

/// <summary>
/// Finds the admin's public key on a machine that is being managed.
/// </summary>
/// <remarks>
/// The key is the one thing that must travel to a machine before it can be reached: it is chosen
/// to ship inside the deployment, not to be accepted over the network, so that no packet on the
/// Wi-Fi can talk a machine into trusting a key. Two places are searched, in order of how
/// deliberately they were put there:
///   1. %ProgramData%\EVBlocker\ssh-admin.pub - written by the elevated setup, the durable copy.
///   2. ssh-admin.pub next to the executable - how it arrives in a deployment, before setup runs.
/// </remarks>
public static class AdminKeySource
{
    public const string FileName = "ssh-admin.pub";

    public static string ProgramDataPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "EVBlocker",
        FileName);

    public static string BesideExecutablePath => Path.Combine(AppContext.BaseDirectory, FileName);

    /// <summary>The admin public key to authorise, or null if this machine has not been given one.</summary>
    public static string? Resolve()
    {
        foreach (string path in new[] { ProgramDataPath, BesideExecutablePath })
        {
            string? key = TryRead(path);
            if (key is not null)
            {
                return key;
            }
        }

        return null;
    }

    /// <summary>
    /// Copies the key into the durable ProgramData location, so a later boot can find it even if
    /// the deployment folder is cleaned. Best effort: a failure here is reported by the caller.
    /// </summary>
    public static void Persist(string publicKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKey);

        Directory.CreateDirectory(Path.GetDirectoryName(ProgramDataPath)!);
        File.WriteAllText(ProgramDataPath, publicKey.Trim());
    }

    private static string? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            string content = File.ReadAllText(path).Trim();
            return content.StartsWith("ssh-", StringComparison.Ordinal) ? content : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
