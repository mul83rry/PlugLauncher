<#
.SYNOPSIS
    یک فایل .plz را روی فروشگاه منتشر می‌کند.

.DESCRIPTION
    حالت پیش‌فرض «استاتیک» است: بسته در ./dist گذاشته می‌شود، فروشگاه استاتیک دوباره ساخته
    می‌شود، خروجی در worktree ی برنچ gh-pages می‌نشیند و یک کامیت/پوش می‌خورد. نه کلیدی لازم
    است نه سروری.

    حالت -Api همان مسیر قدیمی (POST به سرویس ASP.NET با هدر X-Api-Key) است و فقط برای وقتی
    نگه داشته شده که بخواهی به فروشگاه VPS برگردی.

.EXAMPLE
    ./tools/publish-plugin.ps1 -File dist/com.pluglauncher.password-1.1.0.plz
    ./tools/publish-plugin.ps1 -File dist/x.plz -NoPush
    ./tools/publish-plugin.ps1 -File dist/x.plz -Api -ApiKey $env:PLUGSTORE_KEY -Overwrite
#>
[CmdletBinding(DefaultParameterSetName = "Static")]
param(
    [Parameter(Mandatory = $true)][string]$File,

    # --- حالت استاتیک (گیت‌هاب Pages) ---
    [Parameter(ParameterSetName = "Static")][string]$Worktree = "../PlugLauncher-pages",
    [Parameter(ParameterSetName = "Static")][string]$Message,
    [Parameter(ParameterSetName = "Static")][switch]$NoPush,

    # --- حالت API (سرور VPS) ---
    [Parameter(ParameterSetName = "Api", Mandatory = $true)][switch]$Api,
    [Parameter(ParameterSetName = "Api")][string]$Server = "https://thehokm.cloud/pluglauncher",
    [Parameter(ParameterSetName = "Api")][string]$ApiKey = $env:PLUGSTORE_KEY,
    [Parameter(ParameterSetName = "Api")][switch]$Overwrite
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $File)) { throw "فایل بسته پیدا نشد: $File" }
$package = (Resolve-Path $File).Path
$repo = Split-Path -Parent $PSScriptRoot

# ---------------------------------------------------------------- حالت API (قدیمی)
if ($Api) {
    if (-not $ApiKey) { throw "کلید انتشار داده نشده (پارامتر -ApiKey یا متغیر PLUGSTORE_KEY)." }

    $url = "$($Server.TrimEnd('/'))/api/v1/plugins"
    if ($Overwrite) { $url += "?overwrite=true" }

    $bytes = [System.IO.File]::ReadAllBytes($package)

    try {
        $response = Invoke-RestMethod -Uri $url -Method Post -Body $bytes `
            -ContentType "application/zip" -Headers @{ "X-Api-Key" = $ApiKey }

        Write-Host "منتشر شد: $($response.id) v$($response.version) ($($response.size) بایت)"
        return $response
    }
    catch {
        $detail = $_.ErrorDetails.Message
        if ($detail) { Write-Host "خطا از سرور: $detail" -ForegroundColor Red }
        throw
    }
}

# ---------------------------------------------------------------- حالت استاتیک
$distDir = Join-Path $repo "dist"
$staticDir = Join-Path $distDir "store-static"
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

# اگر بسته از جای دیگری آمده، اول کنار بقیه‌ی بسته‌ها کپی می‌شود؛ build-static-store کل پوشه را می‌خواند
$target = Join-Path $distDir (Split-Path -Leaf $package)
if ($target -ne $package) { Copy-Item $package $target -Force }

& (Join-Path $PSScriptRoot "build-static-store.ps1") -Packages $distDir -Output $staticDir -Clean

# worktree ی gh-pages؛ اگر نبود ساخته می‌شود (برنچ لوکال یا ریموت، هر کدام موجود بود)
if (-not (Test-Path $Worktree)) {
    $branches = & git -C $repo branch --list --all gh-pages remotes/origin/gh-pages
    if ($branches) { & git -C $repo worktree add $Worktree gh-pages }
    else { & git -C $repo worktree add --orphan -b gh-pages $Worktree }
    if ($LASTEXITCODE -ne 0) { throw "ساخت worktree شکست خورد: $Worktree" }
}
$pages = (Resolve-Path $Worktree).Path

# محتوای قبلی پاک می‌شود تا بسته‌ی حذف‌شده در worktree جا نماند (.git و .nojekyll می‌مانند)
Get-ChildItem -Path $pages -Force |
    Where-Object { $_.Name -ne ".git" -and $_.Name -ne ".nojekyll" } |
    Remove-Item -Recurse -Force

Copy-Item -Path (Join-Path $staticDir "*") -Destination $pages -Recurse -Force

# بدون .nojekyll، Pages هر فایل/پوشه‌ای که با _ شروع شود را نادیده می‌گیرد
$nojekyll = Join-Path $pages ".nojekyll"
if (-not (Test-Path $nojekyll)) { New-Item -ItemType File $nojekyll | Out-Null }

& git -C $pages add -A
if (-not (& git -C $pages status --porcelain)) {
    Write-Host "چیزی برای انتشار نیست؛ فروشگاه همین حالا به‌روز است."
    return
}

if (-not $Message) {
    $leaf = [System.IO.Path]::GetFileNameWithoutExtension($package)
    $Message = "Publish $leaf"
}
& git -C $pages commit -q -m $Message
if ($LASTEXITCODE -ne 0) { throw "کامیت روی gh-pages شکست خورد." }
Write-Host "کامیت شد روی gh-pages: $Message"

if ($NoPush) {
    Write-Host "پوش نشد (-NoPush). برای انتشار: git -C $pages push origin gh-pages"
    return
}

& git -C $pages push origin gh-pages
if ($LASTEXITCODE -ne 0) { throw "پوش gh-pages شکست خورد (اعتبارنامه‌ی گیت را چک کن)." }
Write-Host "منتشر شد: https://mul83rry.github.io/PlugLauncher/"
