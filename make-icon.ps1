# Generates DeskFrame\Icon\ico.ico (multi-res) and DeskFrame\ico.png from scratch.
# Design: deep slate rounded square with three stacked white "shelf" bars inside.
# Works on dark and light wallpapers; reads cleanly at 16x16.

Add-Type -AssemblyName System.Drawing

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = @()

foreach ($Size in $sizes) {
    [int]$S = [int]$Size

    $bmp = New-Object System.Drawing.Bitmap $S, $S, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # --- Outer rounded square background ---
    $inset = [int]([Math]::Max(1, [Math]::Round($S * 0.06)))
    $rectX = $inset
    $rectY = $inset
    $rectW = $S - (2 * $inset)
    $rectH = $S - (2 * $inset)
    $outerR = [int][Math]::Round($S * 0.22)
    $od = 2 * $outerR
    if ($od -ge $rectW) { $od = $rectW - 1 }
    if ($od -ge $rectH) { $od = $rectH - 1 }
    if ($od -lt 2) { $od = 2 }

    $outerPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $outerPath.AddArc($rectX, $rectY, $od, $od, 180, 90)
    $outerPath.AddArc($rectX + $rectW - $od, $rectY, $od, $od, 270, 90)
    $outerPath.AddArc($rectX + $rectW - $od, $rectY + $rectH - $od, $od, $od, 0, 90)
    $outerPath.AddArc($rectX, $rectY + $rectH - $od, $od, $od, 90, 90)
    $outerPath.CloseFigure()

    $bgColor = [System.Drawing.Color]::FromArgb(255, 35, 41, 54)
    $bgBrush = New-Object System.Drawing.SolidBrush $bgColor
    $g.FillPath($bgBrush, $outerPath)
    $bgBrush.Dispose()
    $outerPath.Dispose()

    # --- Three white "shelf" bars ---
    $marginX = [int][Math]::Round($S * 0.22)
    $barW    = $S - (2 * $marginX)
    $barH    = [int]([Math]::Max(2, [Math]::Round($S * 0.10)))
    $gapY    = [int]([Math]::Max(2, [Math]::Round($S * 0.08)))
    $totalH  = ($barH * 3) + ($gapY * 2)
    $startY  = [int][Math]::Round(($S - $totalH) / 2)
    $barR    = [int]([Math]::Max(1, [Math]::Round($barH * 0.40)))

    # Shrink so top is shorter than middle, middle shorter than bottom.
    $shrink2 = [int][Math]::Round($S * 0.08)
    $shrink1 = [int][Math]::Round($S * 0.04)
    $shrinks = @($shrink2, $shrink1, 0)

    $whiteBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 255, 255))

    for ($i = 0; $i -lt 3; $i++) {
        $sh = [int]$shrinks[$i]
        $bx = $marginX + $sh
        $bw = $barW - (2 * $sh)
        $by = $startY + ($i * ($barH + $gapY))
        $bd = 2 * $barR
        if ($bd -ge $bw) { $bd = $bw - 1 }
        if ($bd -ge $barH) { $bd = $barH - 1 }
        if ($bd -lt 2) { $bd = 2 }

        $barPath = New-Object System.Drawing.Drawing2D.GraphicsPath
        $barPath.AddArc($bx, $by, $bd, $bd, 180, 90)
        $barPath.AddArc($bx + $bw - $bd, $by, $bd, $bd, 270, 90)
        $barPath.AddArc($bx + $bw - $bd, $by + $barH - $bd, $bd, $bd, 0, 90)
        $barPath.AddArc($bx, $by + $barH - $bd, $bd, $bd, 90, 90)
        $barPath.CloseFigure()
        $g.FillPath($whiteBrush, $barPath)
        $barPath.Dispose()
    }

    $whiteBrush.Dispose()
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    $bmp.Dispose()
    $ms.Dispose()
}

# --- Pack into a multi-resolution .ico ---
$out = New-Object System.IO.MemoryStream
$bw  = New-Object System.IO.BinaryWriter $out
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$dataOffset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $blob = $pngs[$i]
    if ($s -ge 256) { $w = 0; $h = 0 } else { $w = $s; $h = $s }
    $bw.Write([byte]$w); $bw.Write([byte]$h); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$blob.Length); $bw.Write([uint32]$dataOffset)
    $dataOffset += $blob.Length
}
foreach ($blob in $pngs) { $bw.Write($blob) }
$bw.Flush()
$icoBytes = $out.ToArray()
$bw.Close(); $out.Close()

$icoPath = Join-Path $PSScriptRoot "DeskFrame\Icon\ico.ico"
[System.IO.File]::WriteAllBytes($icoPath, $icoBytes)
Write-Host "wrote $icoPath ($($icoBytes.Length) bytes, $($sizes.Count) sizes)"

# Save the 256 PNG (last in list) standalone for PackageIcon + Resource refs.
$pngPath = Join-Path $PSScriptRoot "DeskFrame\ico.png"
[System.IO.File]::WriteAllBytes($pngPath, $pngs[$pngs.Count - 1])
$pngPath2 = Join-Path $PSScriptRoot "DeskFrame\Icon\ico.png"
[System.IO.File]::WriteAllBytes($pngPath2, $pngs[$pngs.Count - 1])
Write-Host "wrote $pngPath and $pngPath2"
