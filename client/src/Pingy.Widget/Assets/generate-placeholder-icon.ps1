# Generates a PLACEHOLDER app/tray icon for Pingy — a radar/ping motif in the
# app's signature cyan (#00F0FF) on the void-dark background (#0C1424).
# Self-generated so it is unambiguously license-clean to commit. Replace
# pingy.ico with a real icon when one is chosen; re-run this to tweak the placeholder.
#
#   powershell -ExecutionPolicy Bypass -File generate-placeholder-icon.ps1

Add-Type -AssemblyName System.Drawing

function Get-RoundedPath {
    param([single]$x, [single]$y, [single]$w, [single]$h, [single]$r)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconPng {
    param([int]$s)

    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $cx = $s / 2.0
    $cy = $s / 2.0

    # background rounded square (inset so the border isn't clipped)
    $inset = [single][Math]::Max(1, $s * 0.04)
    $rectW = [single]($s - 2 * $inset)
    $radius = [single]($s * 0.20)
    $bgPath = Get-RoundedPath $inset $inset $rectW $rectW $radius
    $bgBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 12, 20, 36))
    $g.FillPath($bgBrush, $bgPath)

    # cyan frame — keeps the icon visible on a pure-black taskbar
    $borderW = [single][Math]::Max(1, $s * 0.035)
    $borderPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(220, 0, 240, 255)), $borderW
    $g.DrawPath($borderPen, $bgPath)

    # concentric ping rings
    $ringW = [single][Math]::Max(1, $s * 0.05)
    $rings = @(
        @{ rad = $s * 0.40; a = 110 },
        @{ rad = $s * 0.24; a = 210 }
    )
    foreach ($ring in $rings) {
        $rad = [single]$ring.rad
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb([int]$ring.a, 0, 240, 255)), $ringW
        $g.DrawEllipse($pen, [single]($cx - $rad), [single]($cy - $rad), [single]($rad * 2), [single]($rad * 2))
        $pen.Dispose()
    }

    # bright center dot — the "ping" origin
    $dot = [single]($s * 0.095)
    $dotBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0, 240, 255))
    $g.FillEllipse($dotBrush, [single]($cx - $dot), [single]($cy - $dot), [single]($dot * 2), [single]($dot * 2))

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = @(256, 64, 48, 32, 16)
$pngs = @{}
foreach ($sz in $sizes) { $pngs[$sz] = New-IconPng $sz }

$outPath = Join-Path $PSScriptRoot 'pingy.ico'
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)

# ICONDIR
$bw.Write([uint16]0)              # reserved
$bw.Write([uint16]1)              # type: 1 = icon
$bw.Write([uint16]$sizes.Count)   # image count

# ICONDIRENTRY per image
$offset = 6 + 16 * $sizes.Count
foreach ($sz in $sizes) {
    $bytes = $pngs[$sz]
    $dim = if ($sz -ge 256) { 0 } else { $sz }   # 0 means 256 in the ICO spec
    $bw.Write([byte]$dim)         # width
    $bw.Write([byte]$dim)         # height
    $bw.Write([byte]0)            # palette size
    $bw.Write([byte]0)            # reserved
    $bw.Write([uint16]1)          # color planes
    $bw.Write([uint16]32)         # bits per pixel
    $bw.Write([uint32]$bytes.Length)
    $bw.Write([uint32]$offset)
    $offset += $bytes.Length
}

# PNG payloads
foreach ($sz in $sizes) { $bw.Write($pngs[$sz]) }

$bw.Flush()
[System.IO.File]::WriteAllBytes($outPath, $ms.ToArray())
$bw.Dispose()

Write-Output "Wrote $outPath ($((Get-Item $outPath).Length) bytes, $($sizes.Count) sizes)"
