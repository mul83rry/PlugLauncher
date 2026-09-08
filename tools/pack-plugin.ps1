<#
.SYNOPSIS
    یک پوشه‌ی پلاگین را به فایل بسته‌ی .plz (همان zip) تبدیل می‌کند.

.EXAMPLE
    ./pack-plugin.ps1 -Path ../plugins/steam-games
    ./pack-plugin.ps1 -Path ../plugins/steam-games -Output ../dist
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Path,
    [string]$Output = "dist"
)

$ErrorActionPreference = "Stop"

$pluginDir = (Resolve-Path $Path).Path
$manifestPath = Join-Path $pluginDir "plugin.json"

if (-not (Test-Path $manifestPath)) { throw "plugin.json در $pluginDir پیدا نشد." }

$manifest = Get-Content $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $manifest.id) { throw "plugin.json فیلد id ندارد." }
if (-not $manifest.version) { throw "plugin.json فیلد version ندارد." }

# یک اسم غلط در platforms یعنی پلاگین روی هیچ سیستمی لود نمی‌شود، و هیچ‌جا هم نمی‌گوید چرا.
# اینجا گرفتنش ارزان است؛ بعد از انتشار، گران.
$known = @("windows", "macos", "linux")
foreach ($platform in @($manifest.platforms)) {
    if ($platform -and ($known -notcontains $platform)) {
        throw "platforms مقدار ناشناخته دارد: «$platform». فقط $($known -join '، ') قابل قبول است."
    }
}

if ($manifest.minCore) {
    try { [version]($manifest.minCore -split '-')[0] | Out-Null }
    catch { throw "minCore یک شماره‌ی نسخه نیست: «$($manifest.minCore)»." }
}

$entry = if ($manifest.entry) { $manifest.entry } else { "main.csx" }
if (-not (Test-Path (Join-Path $pluginDir $entry))) { throw "فایل ورودی «$entry» در پوشه‌ی پلاگین نیست." }

if (-not (Test-Path $Output)) { New-Item -ItemType Directory -Path $Output -Force | Out-Null }
$outputDir = (Resolve-Path $Output).Path
$package = Join-Path $outputDir "$($manifest.id)-$($manifest.version).plz"

if (Test-Path $package) { Remove-Item $package -Force }

# فایل‌های بیلد/کش نباید داخل بسته بروند
$temp = Join-Path ([System.IO.Path]::GetTempPath()) ("plz-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $temp | Out-Null

try {
    Copy-Item -Path (Join-Path $pluginDir "*") -Destination $temp -Recurse -Force
    Get-ChildItem -Path $temp -Recurse -Force -Include ".git", "bin", "obj", "*.user" |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

    # Compress-Archive فقط پسوند .zip را می‌پذیرد؛ اول zip می‌سازیم بعد به .plz تغییر نام می‌دهیم
    $zip = [System.IO.Path]::ChangeExtension($package, ".zip")
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $temp "*") -DestinationPath $zip -CompressionLevel Optimal
    Move-Item -Path $zip -Destination $package -Force
}
finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}

$size = (Get-Item $package).Length
$hash = (Get-FileHash $package -Algorithm SHA256).Hash.ToLower()

Write-Host "بسته ساخته شد: $package"
Write-Host "  شناسه : $($manifest.id)"
Write-Host "  نسخه  : $($manifest.version)"
Write-Host "  حجم   : $size بایت"
Write-Host "  sha256: $hash"

$package
