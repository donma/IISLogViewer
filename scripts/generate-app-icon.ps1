# 產生 IIS Log Explorer 應用程式圖示 app.ico（多尺寸：16/24/32/48/64/128/256）
# 設計：藍色漸層圓角方底 + 三條日誌條紋 + 偵測亮點
Add-Type -AssemblyName System.Drawing

function New-IconFrame([int]$size, [string]$path) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $margin = [float]($size * 0.05)
    $radius = [float]($size * 0.20)
    $w = $size - 2 * $margin
    $h = $size - 2 * $margin
    $rect = New-Object System.Drawing.RectangleF($margin, $margin, $w, $h)
    $pathObj = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $radius
    $pathObj.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $pathObj.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $pathObj.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $pathObj.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $pathObj.CloseFigure()

    $top = [System.Drawing.Color]::FromArgb(255, 52, 130, 246)
    $bottom = [System.Drawing.Color]::FromArgb(255, 20, 71, 200)
    $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $top, $bottom, 55)
    $g.FillPath($gradient, $pathObj)

    $shine = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(38, 255, 255, 255))
    $shineRect = New-Object System.Drawing.RectangleF($rect.X, $rect.Y, $rect.Width, [float]($rect.Height * 0.45))
    $g.FillRectangle($shine, $shineRect)

    $barH = [float]($size * 0.105)
    $barHalf = $barH / 2
    $gap = [float]($size * 0.115)
    $startY = [float]($rect.Y + $rect.Height * 0.24)
    $barBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(245, 255, 255, 255))
    $accentBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 96, 220, 205))

    $lines = @(
        @{ Xf = 0.20; Wf = 0.58 },
        @{ Xf = 0.22; Wf = 0.44 },
        @{ Xf = 0.20; Wf = 0.60 }
    )
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $xf = $lines[$i].Xf
        $wf = $lines[$i].Wf
        $x = [float]($rect.X + $rect.Width * $xf)
        $bw = [float]($rect.Width * $wf)
        $barRect = New-Object System.Drawing.RectangleF($x, $startY, $bw, $barH)
        $barPath = New-Object System.Drawing.Drawing2D.GraphicsPath
        $barPath.AddArc($barRect.X, $barRect.Y, $barH, $barH, 180, 180)
        $barPath.AddLine($barRect.X + $barHalf, $barRect.Y, $barRect.Right - $barHalf, $barRect.Y)
        $barPath.AddArc($barRect.Right - $barH, $barRect.Y, $barH, $barH, 0, 180)
        $barPath.AddLine($barRect.Right - $barHalf, $barRect.Bottom, $barRect.X + $barHalf, $barRect.Bottom)
        $barPath.CloseFigure()
        if ($i -eq 2) {
            $g.FillPath($accentBrush, $barPath)
        } else {
            $g.FillPath($barBrush, $barPath)
        }
        $startY += $barH + $gap
    }

    $dotR = [float]($size * 0.085)
    $dotX = [float]($rect.Right - $dotR * 1.9)
    $dotY = [float]($rect.Y + $dotR * 1.9)
    $dotBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 250, 204, 21))
    $g.FillEllipse($dotBrush, $dotX - $dotR, $dotY - $dotR, $dotR * 2, $dotR * 2)

    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$base = Join-Path $env:TEMP 'iislog-icon'
New-Item -ItemType Directory -Force -Path $base | Out-Null
$pngFiles = @()
foreach ($s in $sizes) {
    $p = Join-Path $base ("frame_{0}.png" -f $s)
    New-IconFrame $s $p
    $pngFiles += [pscustomobject]@{ Size = $s; Path = $p }
}

function ConvertTo-DibBytes([string]$pngPath) {
    $img = [System.Drawing.Image]::FromFile($pngPath)
    $bm = New-Object System.Drawing.Bitmap($img)
    $width = $bm.Width
    $height = $bm.Height
    $mem = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($mem)
    $bw.Write([int32]40)
    $bw.Write($width)
    $bw.Write($height * 2)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([int32]0)
    $bw.Write([int32]($width * $height * 4))
    $bw.Write([int32]0)
    $bw.Write([int32]0)
    $bw.Write([int32]0)
    $bw.Write([int32]0)
    for ($y = $height - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $width; $x++) {
            $c = $bm.GetPixel($x, $y)
            $bw.Write([byte]$c.B)
            $bw.Write([byte]$c.G)
            $bw.Write([byte]$c.R)
            $bw.Write([byte]$c.A)
        }
    }
    $andBytes = [int](($width * $height) / 8)
    for ($i = 0; $i -lt $andBytes; $i++) { $bw.Write([byte]0) }
    $bw.Flush()
    $result = $mem.ToArray()
    $bm.Dispose()
    $img.Dispose()
    $mem.Dispose()
    Write-Output -NoEnumerate $result
}

$iconEntries = @()
foreach ($f in $pngFiles | Sort-Object Size) {
    if ($f.Size -eq 256) {
        $data = [System.IO.File]::ReadAllBytes($f.Path)
    } else {
        $data = ConvertTo-DibBytes $f.Path
    }
    $iconEntries += [pscustomobject]@{ Size = $f.Size; Data = [byte[]]$data }
}

$ms = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($ms)
$w.Write([uint16]0)
$w.Write([uint16]1)
$w.Write([uint16]$iconEntries.Count)

$offset = 6 + 16 * $iconEntries.Count
$entryList = @()
foreach ($e in $iconEntries) {
    $entry = [ordered]@{
        Width = if ($e.Size -ge 256) { 0 } else { $e.Size }
        Height = if ($e.Size -ge 256) { 0 } else { $e.Size }
        Offset = $offset
        Length = $e.Data.Length
    }
    $entryList += $entry
    $offset += $e.Data.Length
}

foreach ($entry in $entryList) {
    $w.Write([byte]$entry.Width)
    $w.Write([byte]$entry.Height)
    $w.Write([byte]0)
    $w.Write([byte]0)
    $w.Write([uint16]1)
    $w.Write([uint16]32)
    $w.Write([uint32]$entry.Length)
    $w.Write([uint32]$entry.Offset)
}
foreach ($e in $iconEntries) {
    $w.Write([byte[]]$e.Data)
}
$w.Flush()
$out = 'D:\AI_PROJECTS\IISLogViewer\src\IISLogExplorer.App\app.ico'
[System.IO.File]::WriteAllBytes($out, $ms.ToArray())
$ms.Dispose()
Write-Host "ICO written: $out ($((Get-Item $out).Length) bytes, frames=$($iconEntries.Count))"

$icon = New-Object System.Drawing.Icon($out)
Write-Host "Icon reload OK: $($icon.Width)x$($icon.Height)"