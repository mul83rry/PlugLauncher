<#
.SYNOPSIS
    یک فایل .plz را روی فروشگاه منتشر می‌کند.

.EXAMPLE
    ./publish-plugin.ps1 -File dist/com.pluglauncher.calculator-1.0.0.plz -ApiKey $env:PLUGSTORE_KEY
    ./publish-plugin.ps1 -File dist/x.plz -Server http://localhost:5100 -Overwrite
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$File,
    [string]$Server = "https://thehokm.cloud/pluglauncher",
    [string]$ApiKey = $env:PLUGSTORE_KEY,
    [switch]$Overwrite
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $File)) { throw "فایل بسته پیدا نشد: $File" }
if (-not $ApiKey) { throw "کلید انتشار داده نشده (پارامتر -ApiKey یا متغیر PLUGSTORE_KEY)." }

$url = "$($Server.TrimEnd('/'))/api/v1/plugins"
if ($Overwrite) { $url += "?overwrite=true" }

$bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $File).Path)

try {
    $response = Invoke-RestMethod -Uri $url -Method Post -Body $bytes `
        -ContentType "application/zip" -Headers @{ "X-Api-Key" = $ApiKey }

    Write-Host "منتشر شد: $($response.id) v$($response.version) ($($response.size) بایت)"
    $response
}
catch {
    $detail = $_.ErrorDetails.Message
    if ($detail) { Write-Host "خطا از سرور: $detail" -ForegroundColor Red }
    throw
}
