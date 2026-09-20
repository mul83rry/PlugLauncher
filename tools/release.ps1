<#
.SYNOPSIS
    Turns the Unreleased changelog section into a real release: stamp, commit, tag, push.

.DESCRIPTION
    The whole ritual in one command, so no step depends on memory:

      fetch + fast-forward pull   (a stale branch once rejected a gh-pages push)
      refuse if the tag exists     (re-tagging moves history)
      refuse if nothing to release (no Unreleased section, or an empty one)
      refuse if CHANGELOG or Directory.Build.props have local edits
      stamp  "## Unreleased"  ->  "## <version> — <today>"
      commit ONLY the changelog and the props — never "git add -A", so work in
      progress sitting in the tree cannot leak into a release commit
      annotated tag, then push master + tag; the Release workflow does the rest

    The version comes from Directory.Build.props. Pass -Version to change it on
    the spot (the props file is bumped in the same commit). The workflow checks
    the tag against the props file anyway — this script makes sure they agree
    before anything is pushed.

.EXAMPLE
    ./tools/release.ps1
    ./tools/release.ps1 -Version 1.8.0
    ./tools/release.ps1 -NoPush     # stamp, commit and tag locally, push later
#>
[CmdletBinding()]
param(
    [string]$Version,
    [switch]$NoPush
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$changelogPath = Join-Path $repo "CHANGELOG.md"
$propsPath = Join-Path $repo "Directory.Build.props"

function Assert-Clean([string]$path, [string]$label) {
    if (git -C $repo status --porcelain -- $path) {
        throw "$label has local edits. Commit or stash them first — this script writes to that file."
    }
}

# ---------- 1. هم‌گام با ریموت ----------

git -C $repo fetch origin --tags --prune 2>&1 | Out-Null

$behind = git -C $repo rev-list --count "HEAD..origin/master"
if ($behind -gt 0) {
    Write-Host "Behind origin/master by $behind commit(s); fast-forwarding..."
    git -C $repo pull --ff-only origin master
    if ($LASTEXITCODE -ne 0) { throw "Pull failed (diverged?). Rebase manually, then run again." }
}

$ahead = git -C $repo rev-list --count "origin/master..HEAD"
if ($ahead -gt 0) { Write-Warning "HEAD is $ahead commit(s) ahead of origin/master; they will be pushed together." }

# ---------- 2. تمیزیِ فایل‌هایی که خودمان می‌نویسیم ----------
# قبل از هر تغییری، نه بعدش — وگرنه اسکریپت ویرایشِ خودش را «ویرایش محلی» می‌بیند

Assert-Clean $changelogPath "CHANGELOG.md"
Assert-Clean $propsPath "Directory.Build.props"

# ---------- 3. نسخه (فقط خواندن؛ نوشتن بعد از همه‌ی محافظ‌ها) ----------

$declared = ([xml](Get-Content $propsPath -Raw)).SelectSingleNode('/Project/PropertyGroup/Version').InnerText
if (-not $declared) { throw "Directory.Build.props has no <Version>." }
if (-not $Version) { $Version = $declared }

# ---------- 4. محافظ‌های محتوا ----------
# همه قبل از اولین نوشتن؛ اجرای ناموفق نباید فایلِ کثیف روی میز بگذارد

if (git -C $repo rev-parse -q --verify "refs/tags/v$Version") {
    throw "Tag v$Version already exists. A moved tag means a moved release — bump the version instead."
}

$changelog = [System.IO.File]::ReadAllText($changelogPath)
$unreleased = $changelog.IndexOf("## Unreleased")
if ($unreleased -lt 0) {
    throw "CHANGELOG.md has no `"## Unreleased`" section — nothing to release."
}

# بخشِ خالی: اولین خطِ غیرخالی بعد از عنوان نباید خودش یک heading دیگر باشد
$after = $changelog.Substring($unreleased + 14)
$firstMeaningful = ($after -split "`r?`n" | Where-Object { $_.Trim() -ne "" } | Select-Object -First 1)
if ($firstMeaningful -match '^#{1,3} ') {
    throw "The Unreleased section is empty — write at least one entry before releasing."
}

# ---------- 5. مهر ----------

if ($Version -ne $declared) {
    $props = [System.IO.File]::ReadAllText($propsPath)
    $new = $props -replace [regex]::Escape("<Version>$declared</Version>"), "<Version>$Version</Version>"
    [System.IO.File]::WriteAllText($propsPath, $new, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Directory.Build.props: $declared -> $Version"
}

$today = (Get-Date).ToString("yyyy-MM-dd")
$heading = "## $Version — $today"
$changelog = $changelog.Replace("## Unreleased", $heading)
[System.IO.File]::WriteAllText($changelogPath, $changelog, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "CHANGELOG.md: Unreleased -> $heading"

# ---------- 6. کامیتِ فقط همین دو فایل ----------

git -C $repo add -- CHANGELOG.md Directory.Build.props

# خروجی صفر یعنی هیچ تفاوتی stage نشده — گاردِ «هدر از قبل مهر شده»
git -C $repo diff --cached --quiet
if ($LASTEXITCODE -eq 0) {
    throw "Nothing staged — the heading was already stamped? Aborting before tagging."
}
git -C $repo commit -q -m "Release $Version"
if ($LASTEXITCODE -ne 0) { throw "Commit failed." }

git -C $repo tag -a "v$Version" -m "PlugLauncher v$Version"
Write-Host "Tagged v$Version at $(git -C $repo rev-parse --short HEAD)"

# ---------- 7. پوش ----------

if ($NoPush) {
    Write-Host "Not pushed (-NoPush). When ready:  git -C `"$repo`" push origin master v$Version"
    return
}

git -C $repo push origin master "v$Version"
if ($LASTEXITCODE -ne 0) { throw "Push failed." }
Write-Host "Pushed. The Release workflow builds from the tag; watch it with:  gh run watch (or Actions tab)"
