<#
.SYNOPSIS
    یک فروشگاه استاتیک (index.json + پوشه‌ی بسته‌ها) از روی فایل‌های .plz می‌سازد.

.DESCRIPTION
    خروجی این اسکریپت را می‌شود روی هر هاست استاتیکی گذاشت — GitHub Pages، jsDelivr، یا
    حتی یک پوشه روی همان nginx. کلاینت فقط دو GET می‌زند: index.json و بعد فایل بسته.
    نه سرور لازم است، نه دیتابیس، نه کلید انتشار.

    از هر بسته فقط آخرین نسخه در index.json می‌آید (مثل API فروشگاه)، ولی همه‌ی نسخه‌ها
    در خروجی کپی می‌شوند تا لینک نسخه‌های قدیمی نشکند.

.EXAMPLE
    ./tools/build-static-store.ps1 -Packages ./dist -Output ./dist/store-static -Clean
#>
[CmdletBinding()]
param(
    [string]$Packages = "./dist",
    [string]$Output = "./dist/store-static",
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (-not (Test-Path $Packages)) { throw "پوشه‌ی بسته‌ها پیدا نشد: $Packages" }
if ($Clean -and (Test-Path $Output)) { Remove-Item $Output -Recurse -Force }
New-Item -ItemType Directory -Force -Path $Output | Out-Null

# ZipArchive روی ویندوز نام ورودی‌ها را با بک‌اسلش برمی‌گرداند (assets\icon.png) ولی مانیفست
# اسلش دارد؛ برای مقایسه هر دو طرف یکدست می‌شوند.
function Get-ZipEntry($zip, [string]$path) {
    $normalized = $path.Replace([char]92, "/")
    return $zip.Entries | Where-Object { $_.FullName.Replace([char]92, "/") -eq $normalized } | Select-Object -First 1
}

# نسخه‌ی «1.2.0-beta» برای مقایسه به 1.2.0 تبدیل می‌شود؛ پیش‌انتشار پایین‌تر از نسخه‌ی نهایی می‌نشیند
function Get-VersionKey([string]$version) {
    $core = ($version -split '-')[0]
    try { return [version]$core } catch { return [version]"0.0.0" }
}

$entries = @()

foreach ($file in Get-ChildItem -Path $Packages -Filter *.plz -File) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($file.FullName)
    try {
        $manifestEntry = Get-ZipEntry $zip "plugin.json"
        if (-not $manifestEntry) { Write-Warning "بدون plugin.json، رد شد: $($file.Name)"; continue }

        $reader = New-Object System.IO.StreamReader($manifestEntry.Open(), [System.Text.Encoding]::UTF8)
        $manifest = $reader.ReadToEnd() | ConvertFrom-Json
        $reader.Dispose()

        $id = $manifest.id
        $version = $manifest.version
        if (-not $id -or -not $version) { Write-Warning "مانیفست ناقص، رد شد: $($file.Name)"; continue }

        $folder = Join-Path $Output "packages/$id/$version"
        New-Item -ItemType Directory -Force -Path $folder | Out-Null
        Copy-Item $file.FullName (Join-Path $folder $file.Name) -Force

        $iconUrl = $null
        if ($manifest.icon) {
            $iconEntry = Get-ZipEntry $zip $manifest.icon
            if ($iconEntry) {
                $iconName = "icon" + [System.IO.Path]::GetExtension($manifest.icon)
                [System.IO.Compression.ZipFileExtensions]::ExtractToFile(
                    $iconEntry, (Join-Path $folder $iconName), $true)
                $iconUrl = "packages/$id/$version/$iconName"
            }
            else { Write-Warning "آیکن اعلام‌شده در بسته نبود: $($file.Name)" }
        }

        # PowerShell 5.1 داخل هش‌تیبل if نمی‌پذیرد، پس مقادیر جدا حساب می‌شوند
        $name = $id
        if ($manifest.name) { $name = $manifest.name }
        $description = ""
        if ($manifest.description) { $description = $manifest.description }
        $author = ""
        if ($manifest.author) { $author = $manifest.author }
        $keywords = @()
        if ($manifest.keywords) { $keywords = @($manifest.keywords) }

        $entries += [pscustomobject]@{
            id          = $id
            name        = $name
            description = $description
            version     = $version
            author      = $author
            keywords    = $keywords
            size        = $file.Length
            sha256      = (Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            publishedAt = $file.LastWriteTimeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ")
            downloads   = 0
            downloadUrl = "packages/$id/$version/$($file.Name)"
            iconUrl     = $iconUrl
            versionKey  = Get-VersionKey $version
        }
    }
    finally { $zip.Dispose() }
}

# فقط آخرین نسخه‌ی هر بسته در فهرست می‌آید
$latest = $entries |
    Group-Object id |
    ForEach-Object { $_.Group | Sort-Object versionKey -Descending | Select-Object -First 1 } |
    Sort-Object name |
    Select-Object id, name, description, version, author, keywords, size, sha256, publishedAt, downloads, downloadUrl, iconUrl

$index = [ordered]@{
    generatedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    total       = @($latest).Count
    page        = 1
    pageSize    = @($latest).Count
    items       = @($latest)
}

$json = $index | ConvertTo-Json -Depth 6
$indexPath = Join-Path $Output "index.json"
[System.IO.File]::WriteAllText($indexPath, $json, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "فروشگاه استاتیک ساخته شد: $Output"
Write-Host "  بسته‌ها: $(@($latest).Count) (از $($entries.Count) فایل)"
foreach ($item in $latest) { Write-Host "  - $($item.id) v$($item.version)  $([math]::Round($item.size/1KB,1)) KB" }
