<#
.SYNOPSIS
    Chụp ảnh cửa sổ EVBlocker, tuỳ chọn chọn sẵn một mục trong thanh điều hướng.

.DESCRIPTION
    Dùng để kiểm tra giao diện sau khi sửa XAML: dựng ảnh thật của cửa sổ thay vì tin rằng
    thay đổi đã có tác dụng.

    Chụp bằng PrintWindow(PW_RENDERFULLCONTENT) chứ không chụp màn hình theo toạ độ. Một tiến
    trình nền không tự đưa cửa sổ lên trước được (Windows chặn việc cướp foreground), nên chụp
    theo toạ độ sẽ lấy đúng vị trí nhưng nhầm cửa sổ — cửa sổ nào đang nằm trên.

    Chọn mục điều hướng bằng UI Automation chứ không bấm chuột vào toạ độ đoán trước, để script
    không hỏng khi bố cục thay đổi.

.PARAMETER Index
    Vị trí mục trong thanh điều hướng, đếm từ 0. Dùng cái này thay vì -Section khi tên mục có
    dấu tiếng Việt: chuỗi có dấu không qua được vòng truyền tham số của shell nguyên vẹn.

.EXAMPLE
    .\tools\capture-ui.ps1 -Out ui.png
    Chụp mục đang mở sẵn.

.EXAMPLE
    .\tools\capture-ui.ps1 -Index 3 -Out settings.png
    Chọn mục thứ tư rồi chụp.
#>
param(
    [string]$Exe = "src\EVBlocker.App\bin\Debug\net8.0-windows\EVBlocker.exe",
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$Section,
    [int]$Index = -1,
    [int]$WaitSeconds = 20
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public struct RECT { public int Left, Top, Right, Bottom; }
public static class CaptureNative {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
}
"@

$exePath = (Resolve-Path $Exe).Path
$proc = Start-Process -FilePath $exePath -PassThru

try {
    $deadline = (Get-Date).AddSeconds($WaitSeconds)
    while ((Get-Date) -lt $deadline) {
        $proc.Refresh()
        if ($proc.HasExited) { throw "Ứng dụng thoát sớm với mã $($proc.ExitCode)" }
        if ($proc.MainWindowHandle -ne 0) { break }
        Start-Sleep -Milliseconds 250
    }
    if ($proc.MainWindowHandle -eq 0) { throw "Không thấy cửa sổ sau $WaitSeconds giây" }

    # Chờ lần quét socket đầu tiên điền dữ liệu vào bảng.
    Start-Sleep -Seconds 2

    if ($Index -ge 0 -or $Section) {
        $auto = [System.Windows.Automation.AutomationElement]
        $root = $auto::FromHandle($proc.MainWindowHandle)

        # Khớp chính ListBoxItem, không phải TextBlock bên trong nó có cùng tên — chỉ item mới
        # hỗ trợ SelectionItemPattern.
        $byType = New-Object System.Windows.Automation.PropertyCondition(
            $auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)

        if ($Index -ge 0) {
            $items = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $byType)
            if ($Index -ge $items.Count) { throw "Chỉ có $($items.Count) mục; index $Index vượt quá" }
            $item = $items.Item($Index)
        } else {
            $byName = New-Object System.Windows.Automation.PropertyCondition($auto::NameProperty, $Section)
            $cond = New-Object System.Windows.Automation.AndCondition($byName, $byType)
            $item = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
            if ($null -eq $item) { throw "Không tìm thấy mục '$Section'" }
        }

        $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 900
    }

    $rect = New-Object RECT
    [void][CaptureNative]::GetWindowRect($proc.MainWindowHandle, [ref]$rect)

    $bmp = New-Object System.Drawing.Bitmap ($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top)
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $gfx.GetHdc()
    try {
        # 2 = PW_RENDERFULLCONTENT, bắt buộc với cửa sổ do DWM tổng hợp như WPF.
        $ok = [CaptureNative]::PrintWindow($proc.MainWindowHandle, $hdc, 2)
    } finally {
        $gfx.ReleaseHdc($hdc)
    }
    if (-not $ok) { throw "PrintWindow thất bại" }

    # System.Drawing cần đường dẫn tuyệt đối, nhưng -Out có thể là tương đối. Ghép với thư mục
    # hiện tại chỉ khi nó chưa phải đường dẫn gốc — ghép vô điều kiện sẽ tạo ra "C:\a\C:\b".
    $outPath = if ([System.IO.Path]::IsPathRooted($Out)) { $Out }
               else { Join-Path (Get-Location).Path $Out }

    $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $gfx.Dispose()
    $bmp.Dispose()

    "Đã chụp $($rect.Right - $rect.Left)x$($rect.Bottom - $rect.Top) -> $Out"
} finally {
    if (-not $proc.HasExited) { $proc.Kill() }
}
