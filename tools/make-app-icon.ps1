<#
.SYNOPSIS
    Vẽ lại src/EVBlocker.App/Assets/app.ico: khiên bảo vệ quả địa cầu, kèm huy hiệu cấm.

.DESCRIPTION
    Vẽ bằng System.Drawing ở từng kích thước thay vì thu nhỏ một ảnh lớn, để ở 16-24 px quả địa
    cầu rút gọn còn một vòng tròn và vẫn đọc được. Frame dưới 256 px ghi dạng DIB 32-bit, chỉ 256
    px là PNG: System.Drawing.Icon (dùng cho icon ở khay) đọc frame PNG nhỏ thành nhiễu.

.EXAMPLE
    .\tools\make-app-icon.ps1 -OutIco src\EVBlocker.App\Assets\app.ico
#>
param([string]$OutIco, [string]$PreviewDir)
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function New-RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-Shield {
    # Shield outline in a 256 design space.
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddBezier(128, 34, 160, 52, 190, 58, 212, 58)
    $p.AddLine(212, 58, 212, 118)
    $p.AddBezier(212, 118, 212, 178, 176, 212, 128, 232)
    $p.AddBezier(128, 232, 80, 212, 44, 178, 44, 118)
    $p.AddLine(44, 118, 44, 58)
    $p.AddBezier(44, 58, 66, 58, 96, 52, 128, 34)
    $p.CloseFigure()
    return $p
}

function Render([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.ScaleTransform($size / 256.0, $size / 256.0)
    $small = $size -le 24

    # Background tile: deep navy.
    $bg = New-RoundRect 8 8 240 240 52
    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 0)), (New-Object System.Drawing.PointF(0, 256)),
        [System.Drawing.Color]::FromArgb(255, 30, 64, 104), [System.Drawing.Color]::FromArgb(255, 12, 30, 52))
    $g.FillPath($bgBrush, $bg)

    # Shield: green, the "protected" colour.
    $shield = New-Shield
    $shBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 34)), (New-Object System.Drawing.PointF(0, 232)),
        [System.Drawing.Color]::FromArgb(255, 52, 211, 153), [System.Drawing.Color]::FromArgb(255, 5, 150, 105))
    $g.FillPath($shBrush, $shield)

    # Globe inside the shield: the internet being guarded.
    $white = [System.Drawing.Color]::White
    $stroke = if ($small) { 16 } else { 9 }
    $pen = New-Object System.Drawing.Pen($white, $stroke)
    $cx = 128; $cy = 128; $r = 50
    $g.DrawEllipse($pen, $cx - $r, $cy - $r, 2 * $r, 2 * $r)
    if (-not $small) {
        $g.DrawEllipse($pen, $cx - 20, $cy - $r, 40, 2 * $r)
        $g.DrawLine($pen, $cx - $r, $cy, $cx + $r, $cy)
        $thin = New-Object System.Drawing.Pen($white, 6)
        $g.DrawLine($thin, $cx - 43, $cy - 25, $cx + 43, $cy - 25)
        $g.DrawLine($thin, $cx - 43, $cy + 25, $cx + 43, $cy + 25)
    }

    # Block badge, bottom-right: outbound traffic is denied by default.
    $bx = 186; $by = 186; $br = if ($small) { 52 } else { 44 }
    $ring = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 12, 30, 52))
    $g.FillEllipse($ring, $bx - $br - 8, $by - $br - 8, 2 * ($br + 8), 2 * ($br + 8))
    $red = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 239, 68, 68))
    $g.FillEllipse($red, $bx - $br, $by - $br, 2 * $br, 2 * $br)
    $barPen = New-Object System.Drawing.Pen($white, $(if ($small) { 20 } else { 14 }))
    $barPen.StartCap = 'Round'; $barPen.EndCap = 'Round'
    $k = $br * 0.52
    $g.DrawLine($barPen, $bx - $k, $by, $bx + $k, $by)

    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $bmp = Render $s
    $ms = New-Object System.IO.MemoryStream
    if ($s -ge 256) {
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    } else {
        # 32-bit DIB: System.Drawing.Icon and older readers misparse PNG frames at small sizes.
        $bw = New-Object System.IO.BinaryWriter($ms)
        $maskStride = [int]([Math]::Ceiling($s / 32.0) * 4)
        $bw.Write([UInt32]40); $bw.Write([Int32]$s); $bw.Write([Int32](2 * $s))
        $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]0)
        $bw.Write([UInt32]($s * $s * 4 + $maskStride * $s))
        $bw.Write([Int32]0); $bw.Write([Int32]0); $bw.Write([UInt32]0); $bw.Write([UInt32]0)
        for ($y = $s - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $s; $x++) {
                $c = $bmp.GetPixel($x, $y)
                $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
            }
        }
        $bw.Write((New-Object byte[] ($maskStride * $s)))
        $bw.Flush()
    }
    if ($PreviewDir -and $s -in 16, 32, 256) { $bmp.Save((Join-Path $PreviewDir "icon-$s.png")) }
    $bmp.Dispose()
    $pngs += , @($s, $ms.ToArray())
}

# ICO container with PNG-compressed frames (supported since Windows Vista).
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
foreach ($p in $pngs) {
    $dim = if ($p[0] -ge 256) { 0 } else { $p[0] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$p[1].Length); $w.Write([UInt32]$offset)
    $offset += $p[1].Length
}
foreach ($p in $pngs) { $w.Write($p[1]) }
$w.Flush()
[System.IO.File]::WriteAllBytes($OutIco, $out.ToArray())
"wrote $OutIco ($($out.Length) bytes)"
