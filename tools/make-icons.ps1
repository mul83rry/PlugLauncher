# Draws the bundled plugin icons so the set stays consistent if one has to be redrawn.
# Same recipe as the password icon: 128x128, dark navy plate, flat teal glyph, no gradients.
#
#   ./tools/make-icons.ps1

Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$plate = [System.Drawing.ColorTranslator]::FromHtml('#222A3A')
$teal  = [System.Drawing.ColorTranslator]::FromHtml('#5CC8D7')
$dim   = [System.Drawing.ColorTranslator]::FromHtml('#3A7C88')

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $d = $r * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-Canvas {
    $bmp = New-Object System.Drawing.Bitmap 128, 128
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'

    $brush = New-Object System.Drawing.SolidBrush $plate
    $path = New-RoundedPath 0 0 128 128 22
    $g.FillPath($brush, $path)
    $path.Dispose(); $brush.Dispose()

    return @{ Bitmap = $bmp; Graphics = $g }
}

function Save-Canvas($canvas, [string]$relativePath) {
    $full = Join-Path $root $relativePath
    New-Item -ItemType Directory -Force -Path (Split-Path $full) | Out-Null
    $canvas.Graphics.Dispose()
    $canvas.Bitmap.Save($full, [System.Drawing.Imaging.ImageFormat]::Png)
    $canvas.Bitmap.Dispose()
    Write-Host ("{0}  ({1} bytes)" -f $relativePath, (Get-Item $full).Length)
}

function Fill([System.Drawing.Graphics]$g, [System.Drawing.Color]$color, $path) {
    $brush = New-Object System.Drawing.SolidBrush $color
    $g.FillPath($brush, $path)
    $brush.Dispose(); $path.Dispose()
}

function Stroke([System.Drawing.Graphics]$g, [System.Drawing.Color]$color, [single]$width, $points) {
    $pen = New-Object System.Drawing.Pen $color, $width
    $pen.StartCap = 'Round'
    $pen.EndCap = 'Round'
    $pen.LineJoin = 'Round'
    $g.DrawLines($pen, [System.Drawing.PointF[]]$points)
    $pen.Dispose()
}

function P([single]$x, [single]$y) { return [System.Drawing.PointF]::new($x, $y) }

# ===== calculator: teal body, keys and display punched back out in the plate colour =====
$c = New-Canvas
Fill $c.Graphics $teal (New-RoundedPath 30 16 68 96 11)
Fill $c.Graphics $plate (New-RoundedPath 39 25 50 18 4)

foreach ($row in 0..2) {
    foreach ($col in 0..2) {
        $x = 39 + ($col * 19)
        $y = 54 + ($row * 19)
        Fill $c.Graphics $plate (New-RoundedPath $x $y 12 12 6)
    }
}
Save-Canvas $c 'plugins/calculator/assets/icon.png'

# ===== programs: a 2x2 app grid, the last tile dimmed so it reads as "and more" =====
$c = New-Canvas
$tiles = @(@(16, 16), @(72, 16), @(16, 72), @(72, 72))

for ($i = 0; $i -lt $tiles.Count; $i++) {
    $color = if ($i -eq 3) { $dim } else { $teal }
    Fill $c.Graphics $color (New-RoundedPath $tiles[$i][0] $tiles[$i][1] 40 40 9)
}
Save-Canvas $c 'plugins/programs/assets/icon.png'
# ===== ssh hosts: a terminal window with the prompt punched back out of it =====
$c = New-Canvas
Fill $c.Graphics $teal (New-RoundedPath 14 28 100 72 12)
Stroke $c.Graphics $plate 9 @((P 36 54), (P 52 64), (P 36 74))
Stroke $c.Graphics $plate 9 @((P 62 74), (P 86 74))
Save-Canvas $c 'plugins/ssh-hosts/assets/icon.png'

# ===== dev toolbox: </> with the slash dimmed so the brackets read first =====
$c = New-Canvas
Stroke $c.Graphics $teal 10 @((P 48 38), (P 26 64), (P 48 90))
Stroke $c.Graphics $teal 10 @((P 80 38), (P 102 64), (P 80 90))
Stroke $c.Graphics $dim 9 @((P 70 32), (P 58 96))
Save-Canvas $c 'plugins/dev-toolbox/assets/icon.png'

# ===== system: a chip, pins on all four sides =====
$c = New-Canvas
Fill $c.Graphics $teal (New-RoundedPath 34 34 60 60 10)
Fill $c.Graphics $plate (New-RoundedPath 50 50 28 28 6)

foreach ($offset in 44, 60, 76) {
    Fill $c.Graphics $teal (New-RoundedPath $offset 18 8 16 3)
    Fill $c.Graphics $teal (New-RoundedPath $offset 94 8 16 3)
    Fill $c.Graphics $teal (New-RoundedPath 18 $offset 16 8 3)
    Fill $c.Graphics $teal (New-RoundedPath 94 $offset 16 8 3)
}
Save-Canvas $c 'plugins/system/assets/icon.png'

# ===== keyboard layout: two key caps, the second one dimmed — the same key, a different letter =====
$c = New-Canvas
Fill $c.Graphics $teal (New-RoundedPath 18 34 54 54 10)
Fill $c.Graphics $plate (New-RoundedPath 30 46 30 30 6)
Fill $c.Graphics $dim (New-RoundedPath 56 40 54 54 10)
Fill $c.Graphics $plate (New-RoundedPath 68 52 30 30 6)
Stroke $c.Graphics $teal 8 @((P 42 100), (P 86 100))
Stroke $c.Graphics $teal 8 @((P 78 92), (P 86 100), (P 78 108))
Save-Canvas $c 'plugins/keyboard-layout/assets/icon.png'
