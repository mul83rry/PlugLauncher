<#
.SYNOPSIS
    Builds the release archive for PlugLauncher.

.DESCRIPTION
    Publishes the app framework-dependent for win-x64, zips it with a top level
    PlugLauncher folder, and writes SHA256SUMS.txt next to the archive.

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
    [string]$Output = "publish"
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

$hash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLower()
$sums = Join-Path $outputDir "SHA256SUMS.txt"
"$hash  $(Split-Path $archive -Leaf)" | Out-File $sums -Encoding utf8

$size = [math]::Round((Get-Item $archive).Length / 1MB, 1)
$files = @(Get-ChildItem $stage -Recurse -File).Count

Write-Host ""
Write-Host "Archive : $archive"
Write-Host "Version : $Version"
Write-Host "Size    : $size MB ($files files, $pluginCount bundled plugins)"
Write-Host "sha256  : $hash"
