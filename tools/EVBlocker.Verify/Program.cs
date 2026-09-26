using System.Diagnostics;
using EVBlocker.Core;
using EVBlocker.Core.Firewall;
using EVBlocker.Core.Safety;

namespace EVBlocker.Verify;

/// <summary>
/// Exercises every privileged operation EVBlocker performs, without enabling outbound blocking.
/// </summary>
/// <remarks>
/// These paths cannot be unit tested: they need administrator rights, and the development machine
/// does not have them. Everything below them is covered by the test suite, so this covers the one
/// layer that is only ever exercised on a real machine - creating a firewall rule, exporting the
/// configuration, and registering a task as LOCAL SYSTEM.
///
/// The default outbound action is never touched. It is read at the start and again at the end,
/// and a difference is reported as a failure: nothing here should be able to change it.
/// </remarks>
internal static class Program
{
    private const string TestRuleApplication = @"C:\Windows\System32\NOTAREALPROGRAM-evblocker-verify.exe";

    private static int _passed;
    private static int _failed;

    private static int Main()
    {
        // The console starts on the machine's OEM code page, which renders Vietnamese as mojibake.
        // A verification tool whose output cannot be read is not much of a verification tool.
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch (IOException)
        {
            // Redirected output has no code page to set; the text is UTF-8 either way.
        }

        Console.WriteLine("EVBlocker — kiểm chứng đường ghi (write path)");
        Console.WriteLine(new string('=', 60));
        Console.WriteLine("Không bật chặn outbound. Mọi thứ tạo ra đều được dọn lại.");
        Console.WriteLine();

        if (!Elevation.IsElevated)
        {
            Console.WriteLine("CHƯA CÓ QUYỀN ADMIN.");
            Console.WriteLine("Mở PowerShell hoặc Terminal bằng 'Run as administrator', rồi chạy lại file này.");
            return 2;
        }

        Check("Đang chạy với quyền Administrator", () => true);

        var policy = new WindowsFirewallPolicy();

        // Recorded first and compared at the end. If any step below changed it, that is the most
        // important thing this tool could possibly report.
        IReadOnlyDictionary<FirewallProfile, FirewallAction> before = policy.GetDefaultOutboundActions();
        Console.WriteLine($"  DefaultOutboundAction trước: {Describe(before)}");
        Console.WriteLine();

        VerifyFirewallRules(policy);
        VerifyBackupAndDeadMan();

        Console.WriteLine();
        IReadOnlyDictionary<FirewallProfile, FirewallAction> after = policy.GetDefaultOutboundActions();
        Check(
            $"DefaultOutboundAction không đổi ({Describe(after)})",
            () => before.All(p => after[p.Key] == p.Value));

        Console.WriteLine();
        Console.WriteLine(new string('=', 60));
        Console.WriteLine($"PASS: {_passed}   FAIL: {_failed}");

        return _failed == 0 ? 0 : 1;
    }

    private static void VerifyFirewallRules(IFirewallPolicy policy)
    {
        Console.WriteLine("-- Firewall: tạo / đọc / xoá rule --");

        string name = FirewallRuleNaming.ForApplication(TestRuleApplication, FirewallAction.Allow);

        var spec = new FirewallRuleSpec
        {
            Name = name,
            Group = FirewallRuleNaming.Group,
            ApplicationPath = TestRuleApplication,
            Direction = FirewallDirection.Outbound,
            Action = FirewallAction.Allow,
            Profiles = FirewallProfiles.All,
            Enabled = true,
            Description = "Rule tạm do EVBlocker.Verify tạo. Nếu còn sót, xoá được an toàn.",
        };

        try
        {
            Check("Tạo rule", () =>
            {
                policy.AddRule(spec);
                return true;
            });

            FirewallRuleSpec? readBack = policy
                .GetRulesInGroup(FirewallRuleNaming.Group)
                .FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

            Check("Đọc lại rule vừa tạo", () => readBack is not null);
            Check("Đường dẫn ứng dụng đúng", () =>
                string.Equals(readBack?.ApplicationPath, TestRuleApplication, StringComparison.OrdinalIgnoreCase));
            Check("Hướng = Outbound", () => readBack?.Direction == FirewallDirection.Outbound);
            Check("Hành động = Allow", () => readBack?.Action == FirewallAction.Allow);
            Check("Profiles = All", () => readBack?.Profiles == FirewallProfiles.All);

            // Independent confirmation. Reading our own write back through the same code would
            // agree with itself even if both sides were wrong.
            Check("PowerShell cũng thấy rule này", () =>
            {
                (int exitCode, string output) = RunPowerShell(
                    $"(Get-NetFirewallRule -Group '{FirewallRuleNaming.Group}' -ErrorAction SilentlyContinue | "
                    + $"Where-Object DisplayName -eq '{name}' | Measure-Object).Count");

                return exitCode == 0 && output.Trim() == "1";
            });
        }
        finally
        {
            Check("Xoá rule", () =>
            {
                policy.RemoveRule(name);
                return true;
            });

            Check("Rule đã biến mất", () => !policy
                .GetRulesInGroup(FirewallRuleNaming.Group)
                .Any(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private static void VerifyBackupAndDeadMan()
    {
        Console.WriteLine();
        Console.WriteLine("-- Backup + dead-man switch --");

        // A temporary directory rather than the real one, so a verification run does not add
        // noise to the backups a user might one day need.
        string directory = Path.Combine(Path.GetTempPath(), $"evblocker-verify-{Guid.NewGuid():N}");
        var backup = new ConfigBackup(directory);
        var deadMan = new ScheduledTaskDeadManSwitch();

        BackupInfo? info = null;

        try
        {
            Check("netsh advfirewall export", () =>
            {
                info = backup.Create();
                return File.Exists(info.Path) && new FileInfo(info.Path).Length > 0;
            });

            Check("List() tìm thấy bản export", () => backup.List().Any(b => b.Path == info!.Path));

            if (info is null)
            {
                return;
            }

            // The longest delay the switch allows, so there is no realistic chance of it firing
            // during the run. Even if it did, it would import the configuration exported moments
            // earlier - the one currently in effect.
            Check("schtasks: đăng ký task dưới SYSTEM", () =>
            {
                deadMan.Arm(info.Path, TimeSpan.FromMinutes(60));
                return true;
            });

            Check("IsArmed() = true", deadMan.IsArmed);

            Check("Task thật sự chạy dưới SYSTEM", () =>
            {
                (int exitCode, string output) = RunPowerShell(
                    $"(Get-ScheduledTask -TaskName '{ScheduledTaskDeadManSwitch.TaskName}' "
                    + "-ErrorAction SilentlyContinue).Principal.UserId");

                return exitCode == 0
                       && output.Contains("SYSTEM", StringComparison.OrdinalIgnoreCase);
            });

            Check("Task sẽ chạy bù nếu lỡ giờ (StartWhenAvailable)", () =>
            {
                (int exitCode, string output) = RunPowerShell(
                    $"(Get-ScheduledTask -TaskName '{ScheduledTaskDeadManSwitch.TaskName}' "
                    + "-ErrorAction SilentlyContinue).Settings.StartWhenAvailable");

                return exitCode == 0 && output.Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
            });
        }
        finally
        {
            Check("Huỷ task", () =>
            {
                deadMan.Disarm();
                return true;
            });

            Check("IsArmed() = false", () => !deadMan.IsArmed());
            Check("Task đã biến mất khỏi Task Scheduler", () =>
            {
                (_, string output) = RunPowerShell(
                    $"(Get-ScheduledTask -TaskName '{ScheduledTaskDeadManSwitch.TaskName}' "
                    + "-ErrorAction SilentlyContinue | Measure-Object).Count");

                return output.Trim() == "0";
            });

            TryDeleteDirectory(directory);
            Check("Đã dọn thư mục backup tạm", () => !Directory.Exists(directory));
        }
    }

    private static void Check(string description, Func<bool> probe)
    {
        bool ok;
        string? detail = null;

        try
        {
            ok = probe();
        }
        catch (Exception ex)
        {
            ok = false;
            detail = ex.Message;
        }

        if (ok)
        {
            _passed++;
            Console.WriteLine($"  [PASS] {description}");
        }
        else
        {
            _failed++;
            Console.WriteLine($"  [FAIL] {description}");

            if (detail is not null)
            {
                Console.WriteLine($"         {detail}");
            }
        }
    }

    private static (int ExitCode, string Output) RunPowerShell(string command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);

        using Process process = Process.Start(startInfo)!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(30_000);

        return (process.ExitCode, output);
    }

    private static string Describe(IReadOnlyDictionary<FirewallProfile, FirewallAction> actions) =>
        string.Join(", ", actions.Select(p => $"{p.Key}={p.Value}"));

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
