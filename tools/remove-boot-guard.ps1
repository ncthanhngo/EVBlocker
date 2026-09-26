<#
.SYNOPSIS
    Gỡ khoá lúc khởi động của EVBlocker, kể cả khi không còn file EVBlocker.exe.

.DESCRIPTION
    Dùng khi máy mất mạng sau khi khởi động lại mà EVBlocker không tự mở khoá được: task khởi
    động bị xoá, file exe bị xoá hoặc hỏng. Script xoá đúng các filter, sublayer và provider WFP
    mà EVBlocker tạo, theo GUID cố định, và không đụng vào gì khác. Không đổi cấu hình tường lửa:
    nếu đang bật chặn trong tường lửa thì vẫn chặn như cũ, chỉ hết khoá lúc khởi động.

    Được EVBlocker chép vào C:\Program Files\EVBlocker\ khi bật chặn. Bản gốc ở tools\ trong repo.

.EXAMPLE
    Mở PowerShell bằng "Run as administrator", rồi:
    powershell -ExecutionPolicy Bypass -File "C:\Program Files\EVBlocker\remove-boot-guard.ps1"
#>
#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'

# The console starts on the OEM code page, which turns Vietnamese into question marks.
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

# Keys must match src/EVBlocker.Core/Firewall/BootGuardFilters.cs; a test checks that they do.
$filterKeys = @(
    '41ffae50-5585-464c-9f0c-2a77e98ad875', '8f077316-4721-42de-8bce-ba21d4cff9c8',
    '384af285-8cee-4c10-ab1e-9ad1881b61d4', '848fc931-3e92-4ccd-8144-3a98cac45983',
    '858c1dad-a459-47c2-9a02-5e0f7798a0ff', '44716c93-0b33-43f1-baa9-da39ebc6c05c',
    'b4e6aecd-b21f-4a3c-b27a-ccd0c05ddfb9', '7b84520e-303e-49f3-92be-e2b4303a829d',
    '7fa9cc1e-6e9f-488d-9a88-55a909836366', 'c0d67494-efc0-4efe-a95d-57c9805ff8a9',
    '49d21a39-f7c0-4975-8523-d36224cd062e', '70642068-44f2-4bed-83a4-c40806102da9',
    '1e5da7c1-54b3-4206-9de4-ee9291b41444', 'ff5cb432-3817-487a-8398-c340adecf7cf'
)
$subLayerKey = 'a5977be2-6e00-42ca-9bad-22f55250fa29'
$providerKey = 'f3e821ab-76df-4bc7-b3d1-6ab40f155fad'

# The calls and the error handling live in C#: PowerShell 5.1 reads 0x8032xxxx literals as
# negative Int32, which makes comparing them against the API's UInt32 results error-prone.
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class EVBlockerBootGuardRemover
{
    const uint RpcCAuthnDefault = 0xFFFFFFFF;
    const uint FilterNotFound = 0x80320003;
    const uint ProviderNotFound = 0x80320005;
    const uint SubLayerNotFound = 0x80320007;

    [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)]
    static extern uint FwpmEngineOpen0(string serverName, uint authnService, IntPtr authIdentity, IntPtr session, out IntPtr engine);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmEngineClose0(IntPtr engine);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmTransactionBegin0(IntPtr engine, uint flags);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmTransactionCommit0(IntPtr engine);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmTransactionAbort0(IntPtr engine);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmFilterDeleteByKey0(IntPtr engine, ref Guid key);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmSubLayerDeleteByKey0(IntPtr engine, ref Guid key);
    [DllImport("fwpuclnt.dll")] static extern uint FwpmProviderDeleteByKey0(IntPtr engine, ref Guid key);

    public static List<string> Remove(string[] filters, string subLayer, string provider)
    {
        var log = new List<string>();
        IntPtr engine;
        Check(FwpmEngineOpen0(null, RpcCAuthnDefault, IntPtr.Zero, IntPtr.Zero, out engine), "open the filtering engine");

        try
        {
            Check(FwpmTransactionBegin0(engine, 0), "begin transaction");
            try
            {
                foreach (string key in filters)
                {
                    Guid g = new Guid(key);
                    log.Add(Result(FwpmFilterDeleteByKey0(engine, ref g), FilterNotFound, "filter " + key));
                }

                Guid s = new Guid(subLayer);
                log.Add(Result(FwpmSubLayerDeleteByKey0(engine, ref s), SubLayerNotFound, "sublayer " + subLayer));

                Guid p = new Guid(provider);
                log.Add(Result(FwpmProviderDeleteByKey0(engine, ref p), ProviderNotFound, "provider " + provider));

                Check(FwpmTransactionCommit0(engine), "commit");
            }
            catch
            {
                FwpmTransactionAbort0(engine);
                throw;
            }
        }
        finally
        {
            FwpmEngineClose0(engine);
        }

        return log;
    }

    static string Result(uint result, uint notFound, string what)
    {
        if (result == 0) return "đã xoá   " + what;
        if (result == notFound) return "không có " + what;
        throw new InvalidOperationException(string.Format("Could not delete {0}: WFP error 0x{1:X8}", what, result));
    }

    static void Check(uint result, string operation)
    {
        if (result != 0)
        {
            throw new InvalidOperationException(string.Format("Could not {0}: WFP error 0x{1:X8}", operation, result));
        }
    }
}
'@

$log = [EVBlockerBootGuardRemover]::Remove($filterKeys, $subLayerKey, $providerKey)
$log | ForEach-Object { Write-Host "  $_" }
Write-Host ''
Write-Host 'Đã gỡ khoá lúc khởi động. Mạng dùng lại được ngay, không cần khởi động lại.' -ForegroundColor Green
Write-Host 'Nếu vẫn không ra được internet, việc chặn trong tường lửa vẫn đang bật - mở EVBlocker để kiểm tra.'
