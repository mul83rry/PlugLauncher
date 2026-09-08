# Draws the application icon and writes it as a multi-size .ico.
#
#   ./tools/make-app-icon.ps1
#
# Same palette as the plugin icons in make-icons.ps1: dark navy plate, flat teal glyph.
# The glyph is a wall plug, drawn once in a 128x128 coordinate space and scaled to every
# size Windows asks for (16 in the tray, 32 in Explorer, 256 in the large icon view).

Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root 'src/PlugLauncher.App/assets/pluglauncher.ico'

$plate = [System.Drawing.ColorTranslator]::FromHtml('#222A3A')
$teal = [System.Drawing.ColorTranslator]::FromHtml('#5CC8D7')

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

function Fill([System.Drawing.Graphics]$g, [System.Drawing.Color]$color, $path) {
    $brush = New-Object System.Drawing.SolidBrush $color
    $g.FillPath($brush, $path)
    $brush.Dispose(); $path.Dispose()
}

# Everything below is written in 128x128 coordinates; ScaleTransform does the rest.
function New-Glyph([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.ScaleTransform($size / 128, $size / 128)

    # the plate corner radius shrinks with the icon on its own, because it is scaled too
    Fill $g $plate (New-RoundedPath 0 0 128 128 22)

    # two prongs
    Fill $g $teal (New-RoundedPath 44 20 13 32 6)
    Fill $g $teal (New-RoundedPath 71 20 13 32 6)

    # plug body and the cable stub leaving the bottom
    Fill $g $teal (New-RoundedPath 32 50 64 46 12)
    Fill $g $teal (New-RoundedPath 56 94 16 16 5)

    $g.Dispose()
    return $bmp
}

# Frame format matters here. System.Drawing.Icon — which is what NotifyIcon uses for the tray
# icon — cannot decode a PNG compressed frame, even though Explorer can. So every size the app
# actually loads is written as a 32-bit DIB, and only the 256x256 frame is PNG, where the size
# saving is real and nothing but the shell ever reads it.
function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width
    $h = $bmp.Height

    $rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
    $data = $bmp.LockBits($rect, 'ReadOnly', 'Format32bppArgb')
    $stride = $data.Stride
    $pixels = New-Object byte[] ($stride * $h)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $bmp.UnlockBits($data)

    # the AND mask is 1 bit per pixel with rows padded to 4 bytes, even when it is unused
    $maskStride = [int]([math]::Floor(($w + 31) / 32) * 4)

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter $stream

    # BITMAPINFOHEADER — the height is doubled because it covers the XOR and AND bitmaps
    $writer.Write([uint32]40)
    $writer.Write([int32]$w)
    $writer.Write([int32]($h * 2))
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]0)
    $writer.Write([uint32]($w * $h * 4 + $maskStride * $h))
    $writer.Write([int32]0)
    $writer.Write([int32]0)
    $writer.Write([uint32]0)
    $writer.Write([uint32]0)

    # DIB rows run bottom-up
    for ($y = $h - 1; $y -ge 0; $y--) { $writer.Write($pixels, $y * $stride, $w * 4) }

    # all-zero mask: the alpha channel already says what is transparent
    $writer.Write((New-Object byte[] ($maskStride * $h)))

    $writer.Flush()
    $bytes = $stream.ToArray()
    $writer.Dispose()
    $stream.Dispose()

    return , $bytes
}

function Get-PngBytes([System.Drawing.Bitmap]$bmp) {
    $stream = New-Object System.IO.MemoryStream
    $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()
    $stream.Dispose()

    return , $bytes
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = @()

foreach ($size in $sizes) {
    $bmp = New-Glyph $size
    $bytes = if ($size -ge 256) { Get-PngBytes $bmp } else { Get-DibBytes $bmp }
    $bmp.Dispose()

    $images += , @{ Size = $size; Bytes = $bytes }
}

New-Item -ItemType Directory -Force -Path (Split-Path $output) | Out-Null
$file = [System.IO.File]::Create($output)
$writer = New-Object System.IO.BinaryWriter $file

try {
    # ICONDIR: reserved, type 1 (icon), image count
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$images.Count)

    # every ICONDIRENTRY is 16 bytes and they all come before the first image
    $offset = 6 + (16 * $images.Count)

    foreach ($image in $images) {
        # 256 is stored as 0 in a single byte, which is the whole reason the field looks odd
        $dimension = if ($image.Size -ge 256) { 0 } else { $image.Size }

        $writer.Write([byte]$dimension)     # width
        $writer.Write([byte]$dimension)     # height
        $writer.Write([byte]0)              # palette size (0 = truecolour)
        $writer.Write([byte]0)              # reserved
        $writer.Write([uint16]1)            # colour planes
        $writer.Write([uint16]32)           # bits per pixel
        $writer.Write([uint32]$image.Bytes.Length)
        $writer.Write([uint32]$offset)

        $offset += $image.Bytes.Length
    }

    foreach ($image in $images) { $writer.Write($image.Bytes) }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}

Write-Host ("{0}  ({1} bytes, {2} sizes)" -f $output, (Get-Item $output).Length, $images.Count)

# A plain PNG next to the .ico. The window and the tray icon are drawn by the UI toolkit now,
# and its decoder is the same one on all three systems — .ico is a Windows format and only the
# exe and the installer still need it.
$png = Join-Path $root 'src/PlugLauncher.App/assets/pluglauncher.png'
$bmp = New-Glyph 256
$bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

Write-Host ("{0}  ({1} bytes)" -f $png, (Get-Item $png).Length)
