using System.Text;
using EVBlocker.Core.Internal;

namespace EVBlocker.Core.Audit;

/// <summary>
/// Reads and writes the "Filtering Platform Connection" audit subcategory through auditpol.exe.
/// </summary>
/// <remarks>
/// auditpol.exe is used because the alternative, LsaSetInformationPolicy / AuditSetSystemPolicy,
/// needs an LSA policy handle and privilege juggling for no practical gain here.
///
/// The subcategory is addressed by GUID, never by name: the display name is localised, so
/// <c>/subcategory:"Filtering Platform Connection"</c> fails outright on non-English Windows.
/// GUID verified against <c>auditpol /list /subcategory:* /v</c>.
/// </remarks>
public sealed class AuditPolicyManager : IAuditPolicy
{
    /// <summary>Filtering Platform Connection. Source of Security events 5157 and 5156.</summary>
    public const string FilteringPlatformConnectionGuid = "{0CCE9226-69AE-11D9-BED3-505054503030}";

    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    public AuditSetting GetConnectionAudit()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"evblocker-auditpol-{Guid.NewGuid():N}.csv");

        try
        {
            ProcessResult result = ProcessRunner.Run(
                "auditpol.exe",
                new[] { "/backup", $"/file:{tempFile}" },
                CommandTimeout);

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"auditpol /backup failed with exit code {result.ExitCode}. "
                    + "Reading audit policy requires administrator rights. "
                    + Describe(result));
            }

            string csv = ReadTextDetectingEncoding(tempFile);

            // A subcategory that has never been configured is absent from the backup, which means
            // no auditing rather than an error.
            return AuditPolBackupParser.FindSetting(csv, FilteringPlatformConnectionGuid)
                   ?? AuditSetting.None;
        }
        finally
        {
            TryDelete(tempFile);
        }
    }

    public void SetConnectionAudit(AuditSetting setting)
    {
        string success = setting.HasFlag(AuditSetting.Success) ? "enable" : "disable";
        string failure = setting.HasFlag(AuditSetting.Failure) ? "enable" : "disable";

        ProcessResult result = ProcessRunner.Run(
            "auditpol.exe",
            new[]
            {
                "/set",
                $"/subcategory:{FilteringPlatformConnectionGuid}",
                $"/success:{success}",
                $"/failure:{failure}",
            },
            CommandTimeout);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"auditpol /set failed with exit code {result.ExitCode}. "
                + "Changing audit policy requires administrator rights. "
                + Describe(result));
        }
    }

    /// <summary>
    /// auditpol writes its backup as UTF-16, so reading it as UTF-8 yields garbage. Decide from
    /// the byte-order mark rather than assuming either encoding.
    /// </summary>
    private static string ReadTextDetectingEncoding(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static string Describe(ProcessResult result)
    {
        string detail = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;

        return string.IsNullOrWhiteSpace(detail) ? string.Empty : detail.Trim();
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover temp file is harmless and must not mask the real result.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
