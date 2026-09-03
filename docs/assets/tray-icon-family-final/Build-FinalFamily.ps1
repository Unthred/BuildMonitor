# Final multi-size tray mascot family (#95) — visual QA + production PNG/ICO sources.
# Does NOT wire runtime. Locked 16px from accepted A1-R1 family.
#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$out = 'C:\src\BuildMonitor\docs\assets\tray-icon-family-final'
$png = Join-Path $out 'png'
$ico = Join-Path $out 'ico'
$preview = Join-Path $out 'preview'
$sheets = Join-Path $out 'sheets'
$locked16 = 'C:\src\BuildMonitor\docs\assets\tray-visual-qa-16px-building-a1\png'
$subtleSrc = 'C:\Users\Simon.McConnell\.cursor\projects\c-src-BuildMonitor\assets\tray-overlay-exp-duck-subtle-states.png'

foreach ($d in @($out, $png, $ico, $preview, $sheets)) {
    New-Item -ItemType Directory -Force -Path $d | Out-Null
}

$states = @('healthy', 'building', 'attention', 'failed', 'neutral')
$sizes = @(16, 20, 24, 32)

function New-Font([string]$name, [float]$em, [System.Drawing.FontStyle]$style = [System.Drawing.FontStyle]::Regular) {
    return New-Object System.Drawing.Font($name, $em, $style, [System.Drawing.GraphicsUnit]::Point)
}
function To-32([System.Drawing.Bitmap]$src) {
    $dst = New-Object System.Drawing.Bitmap $src.Width, $src.Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($dst)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImageUnscaled($src, 0, 0)
    $g.Dispose()
    return $dst
}
function Clear-NearBlack([System.Drawing.Bitmap]$bmp, [int]$t = 45) {
    for ($y = 0; $y -lt $bmp.Height; $y++) {
        for ($x = 0; $x -lt $bmp.Width; $x++) {
            $p = $bmp.GetPixel($x, $y)
            if ($p.A -gt 0 -and $p.R -le $t -and $p.G -le $t -and $p.B -le $t) {
                $bmp.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(0, 0, 0, 0))
            }
        }
    }
}
function Clear-NearWhite([System.Drawing.Bitmap]$bmp, [int]$t = 242) {
    for ($y = 0; $y -lt $bmp.Height; $y++) {
        for ($x = 0; $x -lt $bmp.Width; $x++) {
            $p = $bmp.GetPixel($x, $y)
            if ($p.A -gt 0 -and $p.R -ge $t -and $p.G -ge $t -and $p.B -ge $t) {
                $bmp.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(0, 0, 0, 0))
            }
        }
    }
}
function Get-OpaqueBounds([System.Drawing.Bitmap]$bmp, [int]$a = 20) {
    $minX = $bmp.Width; $minY = $bmp.Height; $maxX = -1; $maxY = -1
    for ($y = 0; $y -lt $bmp.Height; $y++) {
        for ($x = 0; $x -lt $bmp.Width; $x++) {
            if ($bmp.GetPixel($x, $y).A -ge $a) {
                if ($x -lt $minX) { $minX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    if ($maxX -lt 0) { return [System.Drawing.Rectangle]::Empty }
    return [System.Drawing.Rectangle]::FromLTRB($minX, $minY, $maxX + 1, $maxY + 1)
}
function Crop-ToContent([System.Drawing.Bitmap]$bmp, [float]$padFrac = 0.06) {
    $b = Get-OpaqueBounds $bmp
    if ($b.IsEmpty) { return To-32 $bmp }
    $pad = [int]([math]::Max($b.Width, $b.Height) * $padFrac)
    $x = [math]::Max(0, $b.X - $pad); $y = [math]::Max(0, $b.Y - $pad)
    $r = [math]::Min($bmp.Width, $b.Right + $pad); $bot = [math]::Min($bmp.Height, $b.Bottom + $pad)
    $w = $r - $x; $h = $bot - $y; $side = [math]::Max($w, $h)
    $sq = New-Object System.Drawing.Bitmap $side, $side, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($sq)
    $g.Clear([System.Drawing.Color]::Transparent)
    $ox = [int](($side - $w) / 2); $oy = [int](($side - $h) / 2)
    $g.DrawImage($bmp, $ox, $oy, (New-Object System.Drawing.Rectangle $x, $y, $w, $h), [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    return $sq
}
function Scale-Nearest([System.Drawing.Bitmap]$src, [int]$size) {
    $dst = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($dst)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $g.DrawImage($src, 0, 0, $size, $size)
    $g.Dispose()
    return $dst
}
function Scale-HQ([System.Drawing.Bitmap]$src, [int]$size) {
    $dst = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($dst)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($src, 0, 0, $size, $size)
    $g.Dispose()
    return $dst
}
function Fit-Duck([System.Drawing.Bitmap]$duck, [int]$size) {
    if ($size -le 20) {
        $mid = Scale-HQ $duck ([math]::Max($size * 2, 32))
        $dst = Scale-Nearest $mid $size
        $mid.Dispose()
        return $dst
    }
    return Scale-HQ $duck $size
}
function Set-Px([System.Drawing.Bitmap]$bmp, [int]$x, [int]$y, [System.Drawing.Color]$c) {
    if ($x -lt 0 -or $y -lt 0 -or $x -ge $bmp.Width -or $y -ge $bmp.Height) { return }
    $bmp.SetPixel($x, $y, $c)
}
function Paint-OutlineThenFill([System.Drawing.Bitmap]$bmp, $pts, [System.Drawing.Color]$outline, [System.Drawing.Color]$fill) {
    $set = @{}; foreach ($p in $pts) { $set["$($p[0]),$($p[1])"] = $true }
    foreach ($p in $pts) {
        $x = $p[0]; $y = $p[1]
        foreach ($d in @(@(-1, 0), @(1, 0), @(0, -1), @(0, 1), @(-1, -1), @(1, -1), @(-1, 1), @(1, 1))) {
            $nx = $x + $d[0]; $ny = $y + $d[1]
            if (-not $set.ContainsKey("$nx,$ny")) { Set-Px $bmp $nx $ny $outline }
        }
    }
    foreach ($p in $pts) { Set-Px $bmp $p[0] $p[1] $fill }
}

# --- Locked 16px pixel glyphs (A1-R1 building) ---
function Glyph16-Healthy([System.Drawing.Bitmap]$bmp) {
    $pts = @(
        @(1, 9), @(2, 9), @(2, 10), @(3, 10), @(3, 11), @(4, 11), @(4, 12),
        @(5, 10), @(5, 11), @(6, 9), @(6, 10), @(7, 8), @(7, 9), @(8, 7), @(8, 8), @(9, 6), @(9, 7)
    )
    Paint-OutlineThenFill $bmp $pts ([System.Drawing.Color]::FromArgb(255, 12, 90, 40)) ([System.Drawing.Color]::FromArgb(255, 250, 250, 250))
}
function Glyph16-Attention([System.Drawing.Bitmap]$bmp) {
    $stem = @(
        @(2, 6), @(3, 6), @(4, 6), @(2, 7), @(3, 7), @(4, 7), @(2, 8), @(3, 8), @(4, 8),
        @(2, 9), @(3, 9), @(4, 9), @(2, 10), @(3, 10), @(4, 10)
    )
    $dot = @(@(2, 13), @(3, 13), @(4, 13), @(2, 14), @(3, 14), @(4, 14))
    $dk = [System.Drawing.Color]::FromArgb(255, 15, 15, 18)
    $wh = [System.Drawing.Color]::FromArgb(255, 255, 255, 255)
    Paint-OutlineThenFill $bmp $stem $dk $wh
    Paint-OutlineThenFill $bmp $dot $dk $wh
}
function Glyph16-Failed([System.Drawing.Bitmap]$bmp) {
    $pts = @(
        @(1, 7), @(2, 7), @(2, 8), @(3, 8), @(3, 9), @(4, 9), @(4, 10), @(5, 10), @(5, 11), @(6, 11), @(6, 12), @(7, 12), @(7, 13),
        @(7, 7), @(6, 7), @(6, 8), @(5, 8), @(5, 9), @(3, 10), @(3, 11), @(2, 11), @(2, 12), @(1, 12), @(1, 13)
    )
    Paint-OutlineThenFill $bmp $pts ([System.Drawing.Color]::FromArgb(255, 255, 255, 255)) ([System.Drawing.Color]::FromArgb(255, 210, 35, 35))
}
function Glyph16-Building-A1R1([System.Drawing.Bitmap]$bmp) {
    $W = [System.Drawing.Color]::FromArgb(255, 255, 255, 255)
    $headFill = [System.Drawing.Color]::FromArgb(255, 85, 55, 35)
    $headLite = [System.Drawing.Color]::FromArgb(255, 180, 180, 185)
    $handleFill = [System.Drawing.Color]::FromArgb(255, 230, 125, 28)
    $head = @(
        @(0, 6), @(1, 6), @(2, 6), @(3, 6), @(4, 6),
        @(0, 7), @(1, 7), @(2, 7), @(3, 7), @(4, 7), @(5, 7),
        @(0, 8), @(1, 8), @(2, 8), @(3, 8), @(4, 8), @(5, 8),
        @(6, 7), @(6, 8)
    )
    $handle = @(
        @(4, 9), @(5, 9), @(5, 10), @(6, 10), @(6, 11), @(7, 11), @(7, 12), @(8, 12), @(8, 13), @(9, 13), @(9, 14)
    )
    Paint-OutlineThenFill $bmp $head $W $headFill
    foreach ($p in @(@(0, 7), @(1, 7), @(2, 7), @(0, 8), @(1, 8), @(2, 8))) { Set-Px $bmp $p[0] $p[1] $headLite }
    Paint-OutlineThenFill $bmp $handle $W $handleFill
}

# --- Size-specific vector glyphs for 20/24/32 (lower-left ~45%, opaque) ---
function Draw-HealthyLarge([System.Drawing.Graphics]$g, [int]$size) {
    $scale = $size / 16.0
    $penOut = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 12, 90, 40)), ([math]::Max(1.5, 1.4 * $scale))
    $penIn = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 250, 250, 250)), ([math]::Max(1.2, 1.1 * $scale))
    foreach ($p in @($penOut, $penIn)) {
        $p.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $p.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $p.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    }
    # check in lower-left quadrant
    $pts = @(
        [System.Drawing.PointF]::new(1.5 * $scale, 9.5 * $scale),
        [System.Drawing.PointF]::new(4.2 * $scale, 12.2 * $scale),
        [System.Drawing.PointF]::new(9.5 * $scale, 6.2 * $scale)
    )
    $g.DrawLines($penOut, $pts)
    $g.DrawLines($penIn, $pts)
    $penOut.Dispose(); $penIn.Dispose()
}
function Draw-AttentionLarge([System.Drawing.Graphics]$g, [int]$size) {
    $s = $size / 16.0
    $fill = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
    $out = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 15, 15, 18)), ([math]::Max(1.4, 1.2 * $s))
    $stemW = 3.0 * $s
    $stemH = 5.2 * $s
    $sx = 2.0 * $s; $sy = 5.5 * $s
    $g.FillRectangle($fill, $sx, $sy, $stemW, $stemH)
    $g.DrawRectangle($out, $sx, $sy, $stemW, $stemH)
    $dot = 3.0 * $s
    $dx = 2.0 * $s; $dy = 12.2 * $s
    $g.FillEllipse($fill, $dx, $dy, $dot, $dot)
    $g.DrawEllipse($out, $dx, $dy, $dot, $dot)
    $fill.Dispose(); $out.Dispose()
}
function Draw-FailedLarge([System.Drawing.Graphics]$g, [int]$size) {
    $s = $size / 16.0
    $penOut = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 255, 255, 255)), ([math]::Max(1.8, 1.6 * $s))
    $penIn = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 210, 35, 35)), ([math]::Max(1.3, 1.15 * $s))
    foreach ($p in @($penOut, $penIn)) {
        $p.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $p.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    }
    $x0 = 1.5 * $s; $y0 = 6.5 * $s; $x1 = 8.5 * $s; $y1 = 13.5 * $s
    $g.DrawLine($penOut, $x0, $y0, $x1, $y1)
    $g.DrawLine($penOut, $x1, $y0, $x0, $y1)
    $g.DrawLine($penIn, $x0, $y0, $x1, $y1)
    $g.DrawLine($penIn, $x1, $y0, $x0, $y1)
    $penOut.Dispose(); $penIn.Dispose()
}
function Draw-Building-A1R1Large([System.Drawing.Graphics]$g, [int]$size) {
    # Asymmetric diagonal hammer: long face left, short peen right, narrow handle down-right
    $s = $size / 16.0
    $outline = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 255, 255, 255)), ([math]::Max(1.3, 1.1 * $s))
    $headBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 85, 55, 35))
    $faceBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 180, 180, 185))
    $handleBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 230, 125, 28))

    $state = $g.Save()
    # Place in lower-left; slight rotate so handle runs down-right
    $g.TranslateTransform(5.2 * $s, 9.0 * $s)
    $g.RotateTransform(-38)

    # Head centered on neck; face extends -left more than peen +right
    $headH = 3.2 * $s
    $faceW = 5.2 * $s   # left projection
    $peenW = 1.4 * $s   # right peen
    $neckX = 0
    # face (left of neck)
    $g.FillRectangle($headBrush, -$faceW, -$headH / 2, $faceW + 0.6 * $s, $headH)
    $g.DrawRectangle($outline, -$faceW, -$headH / 2, $faceW + 0.6 * $s, $headH)
    # metal face strip
    $g.FillRectangle($faceBrush, -$faceW + 0.15 * $s, -$headH / 2 + 0.35 * $s, 2.2 * $s, $headH - 0.7 * $s)
    # peen (right, shorter)
    $g.FillRectangle($headBrush, 0.2 * $s, -$headH / 2 + 0.35 * $s, $peenW, $headH - 0.7 * $s)
    $g.DrawRectangle($outline, 0.2 * $s, -$headH / 2 + 0.35 * $s, $peenW, $headH - 0.7 * $s)

    # Handle narrower, down from neck
    $hw = 1.55 * $s
    $hh = 7.2 * $s
    $g.FillRectangle($handleBrush, -$hw / 2, 0.15 * $s, $hw, $hh)
    $g.DrawRectangle($outline, -$hw / 2, 0.15 * $s, $hw, $hh)

    $g.Restore($state)
    $outline.Dispose(); $headBrush.Dispose(); $faceBrush.Dispose(); $handleBrush.Dispose()
}

function New-Icon([System.Drawing.Bitmap]$duck, [string]$state, [int]$size) {
    $bmp = Fit-Duck $duck $size
    if ($size -eq 16) {
        switch ($state) {
            'healthy' { Glyph16-Healthy $bmp }
            'building' { Glyph16-Building-A1R1 $bmp }
            'attention' { Glyph16-Attention $bmp }
            'failed' { Glyph16-Failed $bmp }
            # neutral: no glyph
        }
        return $bmp
    }
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    switch ($state) {
        'healthy' { Draw-HealthyLarge $g $size }
        'building' { Draw-Building-A1R1Large $g $size }
        'attention' { Draw-AttentionLarge $g $size }
        'failed' { Draw-FailedLarge $g $size }
    }
    $g.Dispose()
    return $bmp
}

function Write-Ico([System.Drawing.Bitmap[]]$bitmaps, [string]$path) {
    $pngBytes = @()
    foreach ($b in $bitmaps) {
        $ms = New-Object System.IO.MemoryStream
        $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngBytes += , $ms.ToArray()
        $ms.Dispose()
    }
    $fs = [System.IO.File]::Create($path)
    $bw = New-Object System.IO.BinaryWriter $fs
    $bw.Write([uint16]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]$pngBytes.Count)
    $offset = 6 + 16 * $pngBytes.Count
    for ($i = 0; $i -lt $bitmaps.Count; $i++) {
        $w = $bitmaps[$i].Width; $h = $bitmaps[$i].Height
        $wb = 0; if ($w -lt 256) { $wb = $w }
        $hb = 0; if ($h -lt 256) { $hb = $h }
        $bw.Write([byte]$wb)
        $bw.Write([byte]$hb)
        $bw.Write([byte]0)
        $bw.Write([byte]0)
        $bw.Write([uint16]1)
        $bw.Write([uint16]32)
        $bw.Write([int]$pngBytes[$i].Length)
        $bw.Write([int]$offset)
        $offset += $pngBytes[$i].Length
    }
    foreach ($p in $pngBytes) { $bw.Write($p) }
    $bw.Flush(); $bw.Dispose(); $fs.Dispose()
}

function New-Sheet([string]$title, [System.Drawing.Color]$bg, [object[]]$rows, [string]$path, [string[]]$colLabels) {
    $cellPad = 14; $labelH = 22; $rowLabelW = 90; $maxDisp = 0
    foreach ($row in $rows) {
        $rh = if ($row.nn) { $row.pixelSize * 8 } else { [math]::Max([int]$row.pixelSize, 40) }
        if ($rh -gt $maxDisp) { $maxDisp = $rh }
    }
    $cols = $rows[0].images.Count
    $width = $rowLabelW + 40 + $cols * ($maxDisp + $cellPad)
    $height = 56
    foreach ($row in $rows) {
        $rh = if ($row.nn) { $row.pixelSize * 8 } else { [math]::Max([int]$row.pixelSize, 40) }
        $height += $rh + $labelH + $cellPad
    }
    $bmp = New-Object System.Drawing.Bitmap ([int]$width), ([int]$height), ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear($bg)
    $fg = if ($bg.GetBrightness() -lt 0.5) { [System.Drawing.Color]::White } else { [System.Drawing.Color]::FromArgb(25, 25, 28) }
    $brush = New-Object System.Drawing.SolidBrush $fg
    $g.DrawString($title, (New-Font 'Segoe UI' 12 ([System.Drawing.FontStyle]::Bold)), $brush, 10, 10)
    $x0 = $rowLabelW
    foreach ($lab in $colLabels) {
        $g.DrawString($lab, (New-Font 'Segoe UI' 9), $brush, $x0, 34)
        $x0 += $maxDisp + $cellPad
    }
    $y = 54
    foreach ($row in $rows) {
        $rh = if ($row.nn) { $row.pixelSize * 8 } else { [math]::Max([int]$row.pixelSize, 40) }
        $g.DrawString($row.label, (New-Font 'Segoe UI' 9), $brush, 6, ($y + $rh / 2 - 6))
        $x = $rowLabelW
        foreach ($img in $row.images) {
            if ($row.nn) {
                $draw = Scale-Nearest $img ($row.pixelSize * 8)
                $g.DrawImage($draw, $x, $y, $draw.Width, $draw.Height)
                $draw.Dispose()
            }
            else {
                $ox = $x + [int](($rh - $row.pixelSize) / 2)
                $oy = $y + [int](($rh - $row.pixelSize) / 2)
                $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
                $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
                $g.DrawImage($img, $ox, $oy, $row.pixelSize, $row.pixelSize)
            }
            $x += $rh + $cellPad
        }
        $y += $rh + $labelH + 4
    }
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $brush.Dispose(); $g.Dispose(); $bmp.Dispose()
    Write-Host "Wrote $path"
}

# Load ducks
Write-Host 'Loading ducks...'
$subtleRaw = [System.Drawing.Bitmap]::FromFile($subtleSrc)
$subtle = To-32 $subtleRaw; Clear-NearBlack $subtle 45; $subtleRaw.Dispose()
$ducks = @{}
$sw = [int]($subtle.Width / 5)
for ($i = 0; $i -lt 5; $i++) {
    $rect = New-Object System.Drawing.Rectangle ($i * $sw), 0, $sw, $subtle.Height
    $cell = $subtle.Clone($rect, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $c = Crop-ToContent $cell 0.08; Clear-NearWhite $c 242
    $ducks[$states[$i]] = $c
    $cell.Dispose()
}
$subtle.Dispose()

# Build family
$family = @{}  # key "state|size"
foreach ($sz in $sizes) {
    foreach ($st in $states) {
        # Prefer locked accepted 16px files when present
        $lockedPath = Join-Path $locked16 ("family-A1-R1-{0}.png" -f $st)
        if ($sz -eq 16 -and (Test-Path $lockedPath)) {
            $img = To-32 ([System.Drawing.Bitmap]::FromFile($lockedPath))
            Write-Host "Locked 16px: $st"
        }
        else {
            $img = New-Icon $ducks[$st] $st $sz
        }
        $family["$st|$sz"] = $img
        $img.Save((Join-Path $png ("tray-{0}-{1}.png" -f $st, $sz)), [System.Drawing.Imaging.ImageFormat]::Png)
        if ($sz -eq 16 -or $sz -eq 20) {
            (Scale-Nearest $img (8 * $sz)).Save((Join-Path $preview ("tray-{0}-{1}-nn8x.png" -f $st, $sz)), [System.Drawing.Imaging.ImageFormat]::Png)
        }
    }
}

# ICO per state: frames 16,20,24,32
foreach ($st in $states) {
    $frames = @()
    foreach ($sz in $sizes) { $frames += , $family["$st|$sz"] }
    $icoPath = Join-Path $ico ("tray-{0}.ico" -f $st)
    Write-Ico $frames $icoPath
    Write-Host "ICO $icoPath frames=$($sizes -join ',')"
}

# Contact sheets
$dark = [System.Drawing.Color]::FromArgb(255, 32, 32, 34)
$light = [System.Drawing.Color]::FromArgb(255, 232, 232, 235)
$colLabs = @('HEA', 'BUI', 'ATT', 'FAI', 'NEU')

foreach ($bgName in @('dark', 'light')) {
    $bg = if ($bgName -eq 'dark') { $dark } else { $light }
    $rowsActual = @()
    $rowsNn = @()
    foreach ($sz in $sizes) {
        $imgs = @(); foreach ($st in $states) { $imgs += , $family["$st|$sz"] }
        $rowsActual += @{ label = "${sz}px"; images = $imgs; pixelSize = $sz; nn = $false }
        if ($sz -le 20) {
            $rowsNn += @{ label = "${sz}px NN8x"; images = $imgs; pixelSize = $sz; nn = $true }
        }
    }
    New-Sheet "Final family ACTUAL sizes - $bgName" $bg $rowsActual (Join-Path $sheets ("FINAL-family-actual-$bgName.png")) $colLabs
    New-Sheet "Final family NN inspection (16/20) - $bgName" $bg $rowsNn (Join-Path $sheets ("FINAL-family-nn-$bgName.png")) $colLabs
    # Combined: actual rows then NN for 16
    $combined = @()
    foreach ($sz in $sizes) {
        $imgs = @(); foreach ($st in $states) { $imgs += , $family["$st|$sz"] }
        $combined += @{ label = "${sz}px"; images = $imgs; pixelSize = $sz; nn = $false }
    }
    $imgs16 = @(); foreach ($st in $states) { $imgs16 += , $family["$st|16"] }
    $combined += @{ label = '16 NN8x'; images = $imgs16; pixelSize = 16; nn = $true }
    New-Sheet "Final complete family checkpoint - $bgName" $bg $combined (Join-Path $sheets ("FINAL-complete-$bgName.png")) $colLabs
}

# README
@"
# Tray icon family — final visual checkpoint (#95)

Accepted 16px language carried to 20/24/32 with **size-specific** artwork (not mechanical scale of 16).

## Locked 16px

| State | Source |
|-------|--------|
| Healthy | HEA white+dk |
| Building | A1-R1 |
| Attention | ATT white+dk |
| Failed | FAI red+wt |
| Neutral | greyscale only |

16px PNGs are copied from ``tray-visual-qa-16px-building-a1/png/family-A1-R1-*.png`` when present.

## Production sources (not wired)

| Asset | Path |
|-------|------|
| PNG per state/size | ``png/tray-{state}-{16,20,24,32}.png`` |
| ICO per state | ``ico/tray-{state}.ico`` — frames **16, 20, 24, 32** (PNG-compressed) |

## Sheets

- ``sheets/FINAL-complete-dark.png`` / ``FINAL-complete-light.png``
- ``sheets/FINAL-family-actual-dark.png`` / ``-light.png``
- ``sheets/FINAL-family-nn-dark.png`` / ``-light.png``

## Size-specific deviations

- **16:** exact accepted pixel-authored rasters.
- **20/24/32:** same semantics; glyphs drawn vector-style in lower-left ~45%; Building hammer uses A1-R1 asymmetric proportions (long left face, short peen, narrow handle) with slightly more geometric clarity as pixels allow. No badges, no translucent full-face overlays.

## Not done

Runtime wiring, traffic-light replacement, deploy, PR/merge.
"@ | Set-Content (Join-Path $out 'README.md') -Encoding UTF8

Write-Host 'DONE'
Get-ChildItem $out -Recurse -File | Sort-Object FullName | Select-Object @{n='Rel';e={$_.FullName.Substring($out.Length+1)}}, Length | Format-Table -AutoSize
