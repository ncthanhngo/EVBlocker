using System.IO;
using System.Security.Cryptography;

namespace EVBlocker.App.Services;

/// <summary>
/// The copy of EVBlocker that SYSTEM runs: under Program Files, where only administrators write.
/// </summary>
/// <remarks>
/// The startup task releases the boot guard, and it runs as SYSTEM. Pointing it at wherever the
/// user happened to launch the app from has two problems. The file moves or is deleted - it is a
/// single portable executable - and the next boot has no network. And a SYSTEM task running a
/// file any user can overwrite is a way for any user to run code as SYSTEM.
///
/// The recovery script is written beside it, so it is on the machine before it is needed.
/// </remarks>
internal static class FixedInstall
{
    public const string RecoveryScriptName = "remove-boot-guard.ps1";

    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "EVBlocker");

    public static string ExecutablePath => Path.Combine(DirectoryPath, "EVBlocker.exe");

    public static string RecoveryScriptPath => Path.Combine(DirectoryPath, RecoveryScriptName);

    /// <summary>
    /// Makes the fixed copy match the running executable and returns its path. Needs elevation.
    /// </summary>
    /// <exception cref="InvalidOperationException">The copy could not be written.</exception>
    public static string Ensure()
    {
        string source = Environment.ProcessPath
            ?? throw new InvalidOperationException("Không xác định được đường dẫn của chính ứng dụng.");

        // Only a single-file build is self-contained in one executable. Anywhere else - bin\Debug,
        // dotnet run - the exe is a launcher that needs the DLLs beside it, and a copy of it alone
        // would fail at every boot: the guard never released, the machine offline. Refusing here
        // stops Enable before the guard is installed.
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "EVBlocker.Core.dll")))
        {
            throw new InvalidOperationException(
                "Bản đang chạy không phải bản phát hành một file, nên không cài được vào Program Files. "
                + "Dùng EVBlocker.exe từ trang phát hành để bật chặn.");
        }

        string staging = ExecutablePath + ".new";

        try
        {
            Directory.CreateDirectory(DirectoryPath);
            WriteRecoveryScript();

            if (!string.Equals(Path.GetFullPath(source), ExecutablePath, StringComparison.OrdinalIgnoreCase)
                && !SameContent(source, ExecutablePath))
            {
                // Staged and then moved, so a failed copy never leaves a truncated executable
                // where the startup task expects a working one.
                File.Copy(source, staging, overwrite: true);
                File.Move(staging, ExecutablePath, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(staging);
            throw new InvalidOperationException(
                $"Không cập nhật được {ExecutablePath}: {ex.Message} "
                + "Đóng mọi cửa sổ EVBlocker đang chạy từ thư mục đó rồi thử lại.",
                ex);
        }

        return ExecutablePath;
    }

    private static void WriteRecoveryScript()
    {
        using Stream resource = typeof(FixedInstall).Assembly.GetManifestResourceStream(RecoveryScriptName)
            ?? throw new InvalidOperationException($"Thiếu {RecoveryScriptName} trong ứng dụng.");
        using FileStream file = File.Create(RecoveryScriptPath);
        resource.CopyTo(file);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <remarks>
    /// Size and modification time first: File.Copy keeps the time, so an unchanged copy is
    /// recognised without hashing two 60 MB files on the UI thread at every elevated launch.
    /// </remarks>
    private static bool SameContent(string first, string second)
    {
        if (!File.Exists(second))
        {
            return false;
        }

        var firstInfo = new FileInfo(first);
        var secondInfo = new FileInfo(second);

        if (firstInfo.Length != secondInfo.Length)
        {
            return false;
        }

        if (firstInfo.LastWriteTimeUtc == secondInfo.LastWriteTimeUtc)
        {
            return true;
        }

        using FileStream a = File.OpenRead(first);
        using FileStream b = File.OpenRead(second);
        return SHA256.HashData(a).AsSpan().SequenceEqual(SHA256.HashData(b));
    }
}
