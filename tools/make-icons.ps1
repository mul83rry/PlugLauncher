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
