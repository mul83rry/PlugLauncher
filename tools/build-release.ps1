<#
.SYNOPSIS
    Builds the release archive for PlugLauncher.

.DESCRIPTION
    Publishes the app framework-dependent for win-x64, zips it with a top level
    PlugLauncher folder, builds an Inno Setup installer from the same output, and writes one
    SHA256SUMS.txt covering both.

    The installer needs ISCC.exe on the machine. GitHub's windows runners have it; if it is
    missing the archive is still produced and only the installer is skipped.

    Framework-dependent on purpose: the runtime is a 60 MB download the user makes once,
    against ~170 MB added to every release if it were bundled. The README says which
    runtime to install and links it.

    PublishSingleFile is deliberately not used. In single file mode the assemblies have no
    path on disk, CsxPluginLoader builds its Roslyn references from TRUSTED_PLATFORM_ASSEMBLIES
    and Assembly.Location, and both come back empty — no .csx plugin would compile.

.EXAMPLE
    ./tools/build-release.ps1
    ./tools/build-release.ps1 -Version 1.0.0
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Runtime = "win-x64",
    [string]$Output = "publish",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $PSScriptRoot
$propsPath = Join-Path $repo "Directory.Build.props"

# Directory.Build.props is the single source of truth; a version passed in has to agree with
# it, otherwise a tag could ship an exe stamped with a different number.
# SelectSingleNode rather than .Project.PropertyGroup.Version, so adding a second
# PropertyGroup to the props file later does not quietly turn this into an array.
$declared = ([xml](Get-Content $propsPath -Raw)).SelectSingleNode('/Project/PropertyGroup/Version').InnerText

if (-not $declared) { throw "Directory.Build.props has no <Version>." }

if ($Version -and $Version -ne $declared) {
    throw "Version mismatch: asked for $Version but Directory.Build.props says $declared. Bump the props file first."
}

$Version = $declared

$outputDir = Join-Path $repo $Output
$stageRoot = Join-Path $outputDir "stage"
$stage = Join-Path $stageRoot "PlugLauncher"
$archive = Join-Path $outputDir "PlugLauncher-$Version-$Runtime.zip"

if (Test-Path $stageRoot) { Remove-Item $stageRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Write-Host "Publishing $Version for $Runtime..."

& dotnet publish (Join-Path $repo "src/PlugLauncher.App/PlugLauncher.App.csproj") `
    -c Release `
    -r $Runtime `
    --self-contained false `
    -p:Version=$Version `
    -o $stage `
    --nologo

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

# The bundled plugins are the reason anyone would run this at all, so fail loudly if the
# csproj copy step ever stops working rather than shipping an empty launcher.
$pluginCount = @(Get-ChildItem (Join-Path $stage "plugins") -Directory -ErrorAction SilentlyContinue).Count
if ($pluginCount -eq 0) { throw "No plugins were copied into the publish output." }

if (Test-Path $archive) { Remove-Item $archive -Force }
Compress-Archive -Path $stage -DestinationPath $archive -CompressionLevel Optimal

# The installer is a second way to get the same files, not a different build. GitHub's
# windows runners ship Inno Setup, so CI always produces it; a developer machine usually
# does not, and that only costs the installer, not the archive.
$installer = $null

if (-not $SkipInstaller) {
    $iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if (-not $iscc) {
        $candidates = @(
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
        )
        $found = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
        if ($found) { $iscc = Get-Command $found }
    }

    if ($iscc) {
        Write-Host "Building the installer with $($iscc.Source)..."

        & $iscc.Source `
            "/DAppVersion=$Version" `
            "/DSourceDir=$stage" `
            "/DOutputDir=$outputDir" `
            (Join-Path $PSScriptRoot "PlugLauncher.iss") | Out-Null

        if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE." }

        $installer = Join-Path $outputDir "PlugLauncher-$Version-setup.exe"
        if (-not (Test-Path $installer)) { throw "ISCC reported success but $installer is missing." }
    }
    else {
        Write-Warning "Inno Setup (ISCC.exe) not found; skipping the installer. The archive was still built."
    }
}

# One checksum file covering everything the release carries, so a user can verify whichever
# one they downloaded.
$artifacts = @($archive)
if ($installer) { $artifacts += $installer }

$sums = Join-Path $outputDir "SHA256SUMS.txt"
$lines = foreach ($file in $artifacts) {
    "$((Get-FileHash $file -Algorithm SHA256).Hash.ToLower())  $(Split-Path $file -Leaf)"
}
# Written by hand rather than with Out-File, for the two things that break `sha256sum -c`:
# under Windows PowerShell "-Encoding utf8" prepends a BOM, and CRLF endings leave a \r stuck
# to the end of every filename.
[System.IO.File]::WriteAllText(
    $sums,
    (($lines -join "`n") + "`n"),
    (New-Object System.Text.UTF8Encoding($false)))

$files = @(Get-ChildItem $stage -Recurse -File).Count

Write-Host ""
Write-Host "Version : $Version"
Write-Host "Payload : $files files, $pluginCount bundled plugins"

foreach ($file in $artifacts) {
    $size = [math]::Round((Get-Item $file).Length / 1MB, 1)
    Write-Host ""
    Write-Host "  $file"
    Write-Host "  $size MB   sha256 $((Get-FileHash $file -Algorithm SHA256).Hash.ToLower())"
}
