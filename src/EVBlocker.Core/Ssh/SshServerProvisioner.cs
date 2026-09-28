using System.Text;
using EVBlocker.Core.Internal;

namespace EVBlocker.Core.Ssh;

/// <summary>The outcome of preparing a machine to accept SSH.</summary>
public sealed record ProvisionResult(bool Succeeded, string Detail);

/// <summary>
/// Prepares this machine to accept SSH from the admin: turns on Windows' own OpenSSH server,
/// opens port 22 to the local network only, and authorises one public key.
/// </summary>
/// <remarks>
/// Windows ships an SSH server as an optional capability; this drives the same steps a person
/// would run by hand, so nothing here is EVBlocker's own protocol - it is the platform's, which
/// is why the whole thing is a documented PowerShell script rather than hand-rolled interop.
///
/// Password login is left on, on purpose. If the key fails to install on some machine, a keyed
/// login would lock the admin out of the one machine they most need to reach; the firewall still
/// holds the door to the local subnet either way.
///
/// Every step is written to be safe to run again: the service is only enabled if present, the
/// firewall rule is replaced, and the key is appended only when it is not already there.
/// </remarks>
public sealed class SshServerProvisioner
{
    /// <summary>Named so a person reading Windows Firewall can see who added it and why.</summary>
    public const string FirewallRuleName = "EVBlocker SSH (LAN)";

    /// <summary>The inbound rule that lets this machine answer discovery probes.</summary>
    public const string DiscoveryRuleName = "EVBlocker Discovery (LAN)";

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Runs the setup for <paramref name="authorizedPublicKey"/>, an OpenSSH public key line.
    /// </summary>
    /// <remarks>
    /// Needs administrator rights. The capability install alone can take minutes, hence the long
    /// timeout; the caller runs this off the UI thread.
    /// </remarks>
    public ProvisionResult Provision(string authorizedPublicKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizedPublicKey);

        string key = authorizedPublicKey.Trim();
        if (!key.StartsWith("ssh-", StringComparison.Ordinal))
        {
            return new ProvisionResult(false, "Khoá công khai không hợp lệ (phải bắt đầu bằng \"ssh-\").");
        }

        ProcessResult result = ProcessRunner.Run(
            "powershell.exe",
            new[]
            {
                "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                "-Command", BuildScript(key),
            },
            Timeout);

        // The script prints DONE on the happy path; anything else is surfaced verbatim so a real
        // failure is not hidden behind a generic message.
        return result.Succeeded && result.StandardOutput.Contains("EVBLOCKER_SSH_DONE", StringComparison.Ordinal)
            ? new ProvisionResult(true, "Đã bật SSH và cài khoá. Cổng 22 chỉ mở cho mạng nội bộ.")
            : new ProvisionResult(false, result.FailureDetail.Length > 0 ? result.FailureDetail : "Không rõ lỗi.");
    }

    /// <summary>
    /// The setup as a PowerShell script. Kept in one place so the exact steps are auditable, and
    /// so the manual runbook in the docs and what the app does can never drift apart.
    /// </summary>
    private static string BuildScript(string key)
    {
        // Single-quoted in PowerShell so nothing in the key is interpreted; a public key has no
        // single quotes, but the doubling keeps that true even if one ever appears.
        string quotedKey = "'" + key.Replace("'", "''", StringComparison.Ordinal) + "'";

        var script = new StringBuilder();
        script.AppendLine("$ErrorActionPreference = 'Stop'");

        // 1. Install the OpenSSH server capability if it is not already there.
        script.AppendLine(
            "$cap = Get-WindowsCapability -Online | Where-Object Name -like 'OpenSSH.Server*'");
        script.AppendLine(
            "if ($cap -and $cap.State -ne 'Installed') { Add-WindowsCapability -Online -Name $cap.Name | Out-Null }");

        // 2. Start it, and keep it starting with Windows.
        script.AppendLine("Set-Service -Name sshd -StartupType Automatic");
        script.AppendLine("Start-Service -Name sshd");

        // 3. Port 22 open to the local subnet only. The capability adds a rule open to Any; it is
        //    removed so the narrower rule is the only one.
        script.AppendLine(
            "Get-NetFirewallRule -DisplayName 'OpenSSH SSH Server*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule");
        script.AppendLine(
            $"Get-NetFirewallRule -DisplayName '{FirewallRuleName}' -ErrorAction SilentlyContinue | Remove-NetFirewallRule");
        script.AppendLine(
            $"New-NetFirewallRule -DisplayName '{FirewallRuleName}' -Direction Inbound -Action Allow " +
            "-Protocol TCP -LocalPort 22 -RemoteAddress LocalSubnet | Out-Null");

        // 3b. The discovery port, so this machine can answer "who runs EVBlocker?" from the subnet.
        script.AppendLine(
            $"Get-NetFirewallRule -DisplayName '{DiscoveryRuleName}' -ErrorAction SilentlyContinue | Remove-NetFirewallRule");
        script.AppendLine(
            $"New-NetFirewallRule -DisplayName '{DiscoveryRuleName}' -Direction Inbound -Action Allow " +
            $"-Protocol UDP -LocalPort {LanDiscoveryProtocol.Port} -RemoteAddress LocalSubnet | Out-Null");

        // 4. Authorise the key for administrators. On Windows an admin's keys live in this one
        //    machine-wide file, not the per-user one, and its ACL must exclude normal users or
        //    sshd refuses it.
        script.AppendLine("$dir = Join-Path $env:ProgramData 'ssh'");
        script.AppendLine("New-Item -ItemType Directory -Force -Path $dir | Out-Null");
        script.AppendLine("$authFile = Join-Path $dir 'administrators_authorized_keys'");
        script.AppendLine($"$key = {quotedKey}");
        script.AppendLine("if (-not (Test-Path $authFile)) { New-Item -ItemType File -Path $authFile | Out-Null }");
        script.AppendLine(
            "$existing = Get-Content $authFile -ErrorAction SilentlyContinue");
        script.AppendLine(
            "if ($existing -notcontains $key) { Add-Content -Path $authFile -Value $key -Encoding ascii }");
        script.AppendLine("icacls $authFile /inheritance:r | Out-Null");
        script.AppendLine("icacls $authFile /grant 'Administrators:F' 'SYSTEM:F' | Out-Null");

        // 5. Password login is deliberately left as it is (on by default) so a key mishap on one
        //    machine does not lock the admin out of it.
        script.AppendLine("Write-Output 'EVBLOCKER_SSH_DONE'");

        return script.ToString();
    }
}
