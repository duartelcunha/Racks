#Requires -Version 5.1
<#
.SYNOPSIS
    Check, sign and publish a Racks draft release that CI built (docs/RELEASING.md).

.DESCRIPTION
    Run on the owner's machine, from an up-to-date clone that has the release tag, after the
    "release" workflow created the draft. The script:
      1. checks the tag on GitHub points at the same commit as the tag in THIS clone,
      2. checks the release is still a draft and finds the successful release.yml run for that
         commit and tag,
      3. downloads the draft's installer + SHA256SUMS.txt and the run's build artifact, and checks
         that all three agree on the installer's SHA-256,
      4. signs the installer with tools/RacksSigner (key from Windows Credential Manager, never a
         file) and verifies the result with the app's own checks,
      5. uploads the manifest and signature to the draft and checks the upload byte for byte,
      6. publishes ONLY after you type the confirmation phrase.

    Uses gh.exe (the GitHub CLI logged in to the personal account), read-only until step 5.
    Nothing secret is passed on a command line.

.EXAMPLE
    .\tools\release-sign.ps1 -Version 1.4.1
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})$')]
    [string]$Version,

    [ValidatePattern('^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$')]
    [string]$Repo = 'duartelcunha/Racks'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$tag = "v$Version"
$installerName = "Racks-Setup-$Version.exe"
$manifestName = "Racks-Setup-$Version.manifest.json"
$sigName = "$manifestName.sig"
$artifactName = "release-$Version"

# gh.exe explicitly: the personal GitHub CLI login, not a shell wrapper of the same name.
$gh = (Get-Command gh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source

# Plain function (no param block) so "--repo"-style flags pass through $args untouched.
function Invoke-Gh {
    $out = & $gh @args
    if ($LASTEXITCODE -ne 0) { throw "gh.exe $($args -join ' ') failed (exit $LASTEXITCODE)" }
    return $out
}

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Step([string]$Text) { Write-Host ""; Write-Host "== $Text" -ForegroundColor Cyan }

# --- 1. Tag: GitHub must agree with this clone -----------------------------------------------------
Step "Checking tag $tag"
$localCommit = (& git -C $root rev-parse --verify --quiet "refs/tags/$tag^{commit}")
if ($LASTEXITCODE -ne 0 -or -not $localCommit) { throw "Tag $tag does not exist in this clone ($root). Fetch tags or create it first." }
$localCommit = $localCommit.Trim()

$ref = Invoke-Gh api "repos/$Repo/git/ref/tags/$tag" | ConvertFrom-Json
if ($ref.object.type -eq 'tag') {
    $remoteCommit = (Invoke-Gh api "repos/$Repo/git/tags/$($ref.object.sha)" | ConvertFrom-Json).object.sha
} else {
    $remoteCommit = $ref.object.sha
}
if ($remoteCommit -ne $localCommit) {
    throw "Tag $tag on GitHub points at $remoteCommit, but in this clone at $localCommit. Stop and investigate."
}
Write-Host "Tag $tag -> $localCommit (GitHub and local agree)"

# --- 2. Draft release and the CI run that built it ------------------------------------------------
Step "Checking the draft release and its CI run"
$release = Invoke-Gh release view $tag --repo $Repo --json 'isDraft,tagName,assets,url' | ConvertFrom-Json
if (-not $release.isDraft) { throw "Release $tag is already published. This script only signs drafts." }
$assetNames = @($release.assets | ForEach-Object { $_.name })
foreach ($required in @($installerName, 'SHA256SUMS.txt')) {
    if ($assetNames -notcontains $required) { throw "Draft $tag has no $required asset." }
}
foreach ($signed in @($manifestName, $sigName)) {
    if ($assetNames -contains $signed) { throw "Draft $tag already has $signed. Delete it from the draft first if you mean to re-sign." }
}

$runs = Invoke-Gh run list --repo $Repo --workflow release.yml --limit 100 --json 'databaseId,headSha,headBranch,event,status,conclusion,url' | ConvertFrom-Json
$run = @($runs | Where-Object {
        $_.headSha -eq $localCommit -and $_.headBranch -eq $tag -and $_.event -eq 'push' -and
        $_.status -eq 'completed' -and $_.conclusion -eq 'success'
    }) | Select-Object -First 1
if (-not $run) { throw "No successful release.yml run for $tag at $localCommit." }
Write-Host "CI run: $($run.url)"

# --- 3. Download and cross-check ------------------------------------------------------------------
Step "Downloading the draft assets and the CI artifact"
$work = Join-Path ([IO.Path]::GetTempPath()) ("racks-release-$Version-" + [guid]::NewGuid().ToString('N'))
$draftDir = Join-Path $work 'draft'
$ciDir = Join-Path $work 'ci'
New-Item -ItemType Directory -Force $draftDir, $ciDir | Out-Null

Invoke-Gh release download $tag --repo $Repo --dir $draftDir --pattern $installerName --pattern 'SHA256SUMS.txt' | Out-Null
Invoke-Gh run download "$($run.databaseId)" --repo $Repo --name $artifactName --dir $ciDir | Out-Null

$installer = Join-Path $draftDir $installerName
$ciInstaller = Join-Path $ciDir (Join-Path 'assets' $installerName)
if (-not (Test-Path -LiteralPath $ciInstaller)) { throw "The CI artifact $artifactName has no assets/$installerName." }

$draftHash = Get-Sha256 $installer
$ciHash = Get-Sha256 $ciInstaller
$sumLine = @(Get-Content -LiteralPath (Join-Path $draftDir 'SHA256SUMS.txt') | Where-Object { $_ -match "^[0-9a-f]{64}  $([regex]::Escape($installerName))$" })
if ($sumLine.Count -ne 1) { throw "SHA256SUMS.txt has no single entry for $installerName." }
$sumsHash = $sumLine[0].Substring(0, 64)
$ciSums = Get-Sha256 (Join-Path $ciDir (Join-Path 'assets' 'SHA256SUMS.txt'))
$draftSums = Get-Sha256 (Join-Path $draftDir 'SHA256SUMS.txt')

Write-Host "draft installer   $draftHash"
Write-Host "SHA256SUMS entry  $sumsHash"
Write-Host "CI artifact       $ciHash"
if ($draftHash -ne $sumsHash -or $draftHash -ne $ciHash) { throw "The installer hashes do not agree. Do NOT sign this release." }
if ($draftSums -ne $ciSums) { throw "The draft's SHA256SUMS.txt differs from the CI run's. Do NOT sign this release." }

# --- 4. Sign --------------------------------------------------------------------------------------
Step "Signing with RacksSigner (key from Windows Credential Manager)"
$signerProject = Join-Path $root 'tools\RacksSigner\RacksSigner.csproj'
& dotnet build $signerProject -c Release -nologo -v q | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Building RacksSigner failed." }
$signer = Join-Path $root 'tools\RacksSigner\bin\Release\net10.0-windows\RacksSigner.dll'

& dotnet $signer sign --installer $installer --version $Version
if ($LASTEXITCODE -ne 0) { throw "Signing failed." }
& dotnet $signer verify --installer $installer --version $Version
if ($LASTEXITCODE -ne 0) { throw "The signed files do not verify." }

# --- 5. Upload to the draft and check the round trip ----------------------------------------------
Step "Uploading the manifest and signature to the draft"
$manifestPath = Join-Path $draftDir $manifestName
$sigPath = Join-Path $draftDir $sigName
Invoke-Gh release upload $tag $manifestPath $sigPath --repo $Repo | Out-Null

$checkDir = Join-Path $work 'roundtrip'
New-Item -ItemType Directory -Force $checkDir | Out-Null
Invoke-Gh release download $tag --repo $Repo --dir $checkDir --pattern $manifestName --pattern $sigName | Out-Null
if ((Get-Sha256 (Join-Path $checkDir $manifestName)) -ne (Get-Sha256 $manifestPath) -or
    (Get-Sha256 (Join-Path $checkDir $sigName)) -ne (Get-Sha256 $sigPath)) {
    throw "The uploaded manifest/signature differ from the local ones. Do not publish."
}

# --- 6. Publish only on explicit confirmation -----------------------------------------------------
Step "Ready to publish"
Write-Host "Release:   $($release.url)"
Write-Host "Commit:    $localCommit"
Write-Host "Installer: $installerName  sha256 $draftHash"
Write-Host "Manifest:  $(Get-Content -Raw -LiteralPath $manifestPath)"
Write-Host ""
$phrase = "publish $tag"
$answer = Read-Host "Type '$phrase' to publish this release to every user (anything else keeps it a draft)"
if ($answer -cne $phrase) {
    Write-Host "Not published. The draft keeps the signature; signed files are in $draftDir"
    exit 1
}

Invoke-Gh release edit $tag --repo $Repo --draft=false --latest | Out-Null
$latest = Invoke-Gh api "repos/$Repo/releases/latest" | ConvertFrom-Json
if ($latest.tag_name -ne $tag) { throw "Published, but releases/latest reports $($latest.tag_name). Check the release page." }
Write-Host ""
Write-Host "Published $tag. Updater clients will now offer it." -ForegroundColor Green
Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
