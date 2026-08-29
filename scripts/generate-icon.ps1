# generate-icon.ps1 - create a simple "LL" app icon (multi-size PNG + ICO)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$iconDir = Join-Path $root 'docs\icons'
$pngDir = Join-Path $iconDir 'png'
$icoPath = Join-Path $root 'src\LlamaDesktop.App\app.ico'
New-Item -ItemType Directory -Path $pngDir -Force | Out-Null

function New-LogoBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $radius = [Math]::Max(6, [int]($size * 0.22))
    $rect = New-Object System.Drawing.RectangleF(0, 0, $size, $size)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 0)),
        (New-Object System.Drawing.PointF($size, $size)),
        [System.Drawing.Color]::FromArgb(255, 19, 122, 99),   # #137A63
        [System.Drawing.Color]::FromArgb(255, 11, 89, 72))    # #0B5948
    $g.FillPath($brush, $path)

    # Bold "LL" mark
    $fontSize = [float]($size * 0.42)
    $font = New-Object System.Drawing.Font('Segoe UI', $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $textRect = New-Object System.Drawing.RectangleF(0, [float]($size * 0.02), $size, $size)
    $white = [System.Drawing.Brushes]::White
    $g.DrawString('LL', $font, $white, $textRect, $sf)

    $font.Dispose(); $sf.Dispose(); $brush.Dispose(); $path.Dispose(); $g.Dispose()
    return $bmp
}

# Generate multi-size PNGs
$sizes = @(16, 32, 48, 64, 128, 256)
$pngPaths = @()
foreach ($s in $sizes) {
    $img = New-LogoBitmap $s
    $p = Join-Path $pngDir "icon-$s.png"
    $img.Save($p, [System.Drawing.Imaging.ImageFormat]::Png)
    $img.Dispose()
    $pngPaths += $p
}
$png256 = Join-Path $pngDir 'icon-256.png'

# Pack PNGs into a single ICO (multi-size, PNG-compressed entries, Win Vista+)
function Pack-Ico([string[]]$pngFiles, [string]$outPath) {
    $images = @()
    foreach ($p in $pngFiles) {
        $bytes = [System.IO.File]::ReadAllBytes($p)
        $img = [System.Drawing.Image]::FromFile($p)
        $images += @{ Width = $img.Width; Height = $img.Height; Bytes = $bytes }
        $img.Dispose()
    }
    $count = $images.Count
    $fs = [System.IO.File]::Create($outPath)
    $bw = New-Object System.IO.BinaryWriter($fs)
    $bw.Write([UInt16]0)         # reserved
    $bw.Write([UInt16]1)         # type: icon
    $bw.Write([UInt16]$count)    # image count
    $offset = 6 + (16 * $count)
    foreach ($img in $images) {
        $w = if ($img.Width -ge 256) { 0 } else { $img.Width }
        $h = if ($img.Height -ge 256) { 0 } else { $img.Height }
        $bw.Write([Byte]$w)
        $bw.Write([Byte]$h)
        $bw.Write([Byte]0)       # color count
        $bw.Write([Byte]0)       # reserved
        $bw.Write([UInt16]1)     # planes
        $bw.Write([UInt16]32)    # bit count
        $bw.Write([UInt32]$img.Bytes.Length)
        $bw.Write([UInt32]$offset)
        $offset += $img.Bytes.Length
    }
    foreach ($img in $images) { $bw.Write($img.Bytes) }
    $bw.Flush(); $bw.Close(); $fs.Close()
}

Pack-Ico @($pngPaths) $icoPath

Write-Host "Generated:"
Write-Host "  PNGs: $pngDir (16/32/48/64/128/256)"
Write-Host "  ICO : $icoPath"
