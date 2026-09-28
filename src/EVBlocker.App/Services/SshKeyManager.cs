using System.Diagnostics;
using System.IO;

namespace EVBlocker.App.Services;

/// <summary>
/// The admin's SSH identity: one keypair kept on this machine, used to sign in to the managed
/// machines without a password.
/// </summary>
/// <remarks>
/// Generated here rather than asked for, per the choice to let the app make the key. The private
/// half never leaves this folder; the public half is what gets installed on the machines and is
/// safe to show and copy.
///
/// ssh-keygen ships with the Windows OpenSSH client, present on Windows 10 and 11 by default. If
/// it is somehow absent, generation reports that instead of failing silently.
/// </remarks>
public sealed class SshKeyManager
{
    private static readonly string KeyDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EVBlocker",
        "ssh");

    /// <summary>The private key. Passed to ssh with -i; its permissions are left to the user profile.</summary>
    public string PrivateKeyPath { get; } = Path.Combine(KeyDirectory, "evblocker_admin");

    public string PublicKeyPath => PrivateKeyPath + ".pub";

    public bool Exists => File.Exists(PrivateKeyPath) && File.Exists(PublicKeyPath);

    /// <summary>The public key line to install on the machines, or null if it has not been made yet.</summary>
    public string? PublicKey => File.Exists(PublicKeyPath) ? File.ReadAllText(PublicKeyPath).Trim() : null;

    /// <summary>
    /// Makes the keypair if it is not already there, and returns the public key.
    /// </summary>
    /// <remarks>
    /// Idempotent: an existing key is kept, because replacing it would orphan every machine that
    /// already trusts the old one. No passphrase, so a click can connect without a prompt - the
    /// private key's safety rests on the account it lives under.
    /// </remarks>
    public string EnsureKey()
    {
        if (Exists)
        {
            return PublicKey!;
        }

        Directory.CreateDirectory(KeyDirectory);

        // A stale half of a previous attempt would make ssh-keygen ask before overwriting and
        // then hang, since nothing is reading its console.
        File.Delete(PrivateKeyPath);
        File.Delete(PublicKeyPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = "ssh-keygen.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-t");
        startInfo.ArgumentList.Add("ed25519");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add(PrivateKeyPath);
        startInfo.ArgumentList.Add("-N");
        startInfo.ArgumentList.Add(string.Empty);
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add($"evblocker-admin@{Environment.MachineName}");

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Không chạy được ssh-keygen.");
            process.WaitForExit((int)TimeSpan.FromSeconds(30).TotalMilliseconds);

            if (!Exists)
            {
                throw new InvalidOperationException(
                    "ssh-keygen không tạo được khoá. " + process.StandardError.ReadToEnd().Trim());
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException(
                "Không tìm thấy ssh-keygen. Cần OpenSSH Client của Windows (có sẵn trên Windows 10/11).");
        }

        return PublicKey!;
    }
}
