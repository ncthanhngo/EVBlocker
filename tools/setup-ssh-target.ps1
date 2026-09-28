<#
.SYNOPSIS
    Chuẩn bị một máy Windows để nhận SSH từ admin: bật OpenSSH Server, mở cổng 22 cho mạng nội
    bộ, và cấp phép một khoá công khai. Giữ nguyên đăng nhập mật khẩu làm dự phòng.

.DESCRIPTION
    Đây là đúng các bước mà nút "Chuẩn bị máy này để nhận SSH" trong app EVBlocker chạy, tách ra
    thành script để đẩy hàng loạt qua GPO / login script. Chạy được nhiều lần, không hại.

    KHÔNG tắt đăng nhập bằng mật khẩu: nếu khoá cài lỗi trên một máy, mật khẩu là đường vào còn lại
    để sửa. Cổng 22 chỉ mở cho LocalSubnet, không ra internet.

.PARAMETER PublicKey
    Nội dung khoá công khai của admin (một dòng, bắt đầu bằng "ssh-").

.PARAMETER ExePath
    Đường dẫn EVBlocker.exe trên máy đích. Nếu có và file tồn tại, script đăng ký tác vụ chạy khi
    mở máy để tự bật lại SSH sau này. Mặc định là bản trong Program Files.

.EXAMPLE
    .\setup-ssh-target.ps1 -PublicKey "ssh-ed25519 AAAA... evblocker-admin@PC-ADMIN"
#>
param(
    [Parameter(Mandatory = $true)][string]$PublicKey,
    [string]$ExePath = (Join-Path $env:ProgramFiles 'EVBlocker\EVBlocker.exe')
)

$ErrorActionPreference = 'Stop'

if ($PublicKey.Trim() -notlike 'ssh-*') {
    throw "Khoá công khai không hợp lệ (phải bắt đầu bằng 'ssh-')."
}

# 1. Cài OpenSSH Server nếu chưa có.
$cap = Get-WindowsCapability -Online | Where-Object Name -like 'OpenSSH.Server*'
if ($cap -and $cap.State -ne 'Installed') {
    Add-WindowsCapability -Online -Name $cap.Name | Out-Null
}

# 2. Bật dịch vụ, cho tự chạy khi mở máy.
Set-Service -Name sshd -StartupType Automatic
Start-Service -Name sshd

# 3. Cổng 22 chỉ cho mạng nội bộ. Bỏ rule mở-cho-mọi-nơi mà capability tự thêm.
Get-NetFirewallRule -DisplayName 'OpenSSH SSH Server*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
Get-NetFirewallRule -DisplayName 'EVBlocker SSH (LAN)' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -DisplayName 'EVBlocker SSH (LAN)' -Direction Inbound -Action Allow `
    -Protocol TCP -LocalPort 22 -RemoteAddress LocalSubnet | Out-Null

# 4. Cấp phép khoá. Khoá của admin trên Windows nằm ở file dùng chung này, và ACL phải loại người
#    dùng thường ra, nếu không sshd từ chối.
$dir = Join-Path $env:ProgramData 'ssh'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$authFile = Join-Path $dir 'administrators_authorized_keys'
$key = $PublicKey.Trim()
if (-not (Test-Path $authFile)) { New-Item -ItemType File -Path $authFile | Out-Null }
if ((Get-Content $authFile -ErrorAction SilentlyContinue) -notcontains $key) {
    Add-Content -Path $authFile -Value $key -Encoding ascii
}
icacls $authFile /inheritance:r | Out-Null
icacls $authFile /grant 'Administrators:F' 'SYSTEM:F' | Out-Null

# 5. Mở cổng dò tìm để máy này trả lời "ai đang chạy EVBlocker?" từ mạng nội bộ.
Get-NetFirewallRule -DisplayName 'EVBlocker Discovery (LAN)' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -DisplayName 'EVBlocker Discovery (LAN)' -Direction Inbound -Action Allow `
    -Protocol UDP -LocalPort 50505 -RemoteAddress LocalSubnet | Out-Null

# 6. Lưu khoá vào ProgramData để tác vụ khởi động tìm thấy, rồi đăng ký tác vụ tự bật lại khi mở máy.
$keyStore = Join-Path $env:ProgramData 'EVBlocker\ssh-admin.pub'
New-Item -ItemType Directory -Force -Path (Split-Path $keyStore) | Out-Null
Set-Content -Path $keyStore -Value $key -Encoding ascii

if (Test-Path $ExePath) {
    $action = New-ScheduledTaskAction -Execute $ExePath -Argument '--ssh-setup'
    $trigger = New-ScheduledTaskTrigger -AtStartup
    $principal = New-ScheduledTaskPrincipal -UserId 'S-1-5-18' -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName 'EVBlocker-SshSetup' -Action $action -Trigger $trigger `
        -Principal $principal -Settings $settings -Force | Out-Null
    $boot = "Đã đăng ký tự bật lại khi mở máy."
} else {
    $boot = "Chưa đăng ký tác vụ khởi động: không thấy $ExePath (truyền -ExePath cho đúng)."
}

Write-Output "Xong. SSH đã bật, cổng 22 mở cho mạng nội bộ, đã cấp phép khoá. Mật khẩu vẫn dùng được làm dự phòng. $boot"
