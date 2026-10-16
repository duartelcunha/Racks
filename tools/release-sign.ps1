#Requires -Version 5.1
<#
.SYNOPSIS
    Check, sign and publish a Racks draft release that CI built (docs/RELEASING.md).

.DESCRIPTION
    Run the INSTALLED copy (%LOCALAPPDATA%\RacksSigner\<commit>\release-sign.ps1, put there by
    tools\install-signer.ps1), never the copy in the repository. It builds nothing and runs only the
    pinned RacksSigner whose files match %LOCALAPPDATA%\RacksSigner\trusted.json.

      1. checks the pinned signer, and that gh.exe is logged in as the repository owner,
      2. verifies the release tag in your clone is signed with your tag signing key
         (%LOCALAPPDATA%\RacksSigner\allowed_signers, outside the repo) and that GitHub has the
         same tag object,
      3. checks the release is still a draft and finds the successful release.yml run for that
         commit and tag,
      4. downloads the draft's installer + SHA256SUMS.txt and the run's build artifact, and checks
         that all three agree on the installer's SHA-256,
      5. shows what changed since the last signed tag (git log, git diff --stat) and asks you to
         type the confirmation phrase. Nothing is signed before that.
      6. signs, verifies, uploads the manifest and signature, checks the upload byte for byte and
         publishes straight away. If anything fails after signing, or the run is stopped
         (Ctrl+C, closing the console), the signed files are deleted from the draft and from disk
         in a finally block: a signed draft is not left behind.

    Uses gh.exe (the GitHub CLI logged in to the personal account). Nothing secret is passed on a
    command line.

.EXAMPLE
    $t = Get-Content "$env:LOCALAPPDATA\RacksSigner\trusted.json" | ConvertFrom-Json
    & "$env:LOCALAPPDATA\RacksSigner\$($t.commit)\release-sign.ps1" -Version 1.4.1 -Clone C:\src\Racks
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})$')]
    [string]$Version,

    # Your clone of the repository, with the signed release tag fetched.
    [Parameter(Mandatory = $true)]
    [string]$Clone,

    [ValidatePattern('^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$')]
    [string]$Repo = 'duartelcunha/Racks',

    [ValidatePattern('^[A-Za-z0-9-]+$')]
    [string]$Owner = 'duartelcunha'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$base = Join-Path $env:LOCALAPPDATA 'RacksSigner'
$allowedSigners = Join-Path $base 'allowed_signers'
$trustedPath = Join-Path $base 'trusted.json'
$Clone = (Resolve-Path -LiteralPath $Clone).Path
$tag = "v$Version"
$installerName = "Racks-Setup-$Version.exe"
$manifestName = "Racks-Setup-$Version.manifest.json"
$sigName = "$manifestName.sig"
$artifactName = "release-$Version"

function Step([string]$Text) { Write-Host ""; Write-Host "== $Text" -ForegroundColor Cyan }

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

# --- 1. Pinned signer and GitHub account ----------------------------------------------------------
Step "Checking the pinned signer"
if (-not (Test-Path -LiteralPath $trustedPath)) { throw "No $trustedPath. Install the signer first (tools\install-signer.ps1)." }
$trusted = Get-Content -Raw -LiteralPath $trustedPath | ConvertFrom-Json
if ($trusted.commit -notmatch '^[0-9a-f]{40}$') { throw "$trustedPath has no valid commit." }
$pinnedDir = [IO.Path]::GetFullPath((Join-Path $base $trusted.commit))
$here = [IO.Path]::GetFullPath($PSScriptRoot)
if (-not [string]::Equals($here.TrimEnd('\'), $pinnedDir.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
    throw "This is not the installed copy of release-sign.ps1. Run: & `"$pinnedDir\release-sign.ps1`" -Version $Version -Clone `"$Clone`""
}
$expected = @{}
foreach ($p in $trusted.files.PSObject.Properties) { $expected[$p.Name] = [string]$p.Value }
$actual = @{}
# -Force: a hidden or system file dropped next to the signer is an unexpected file too.
Get-ChildItem -LiteralPath $pinnedDir -Recurse -File -Force | ForEach-Object {
    $actual[$_.FullName.Substring($pinnedDir.Length + 1)] = Get-Sha256 $_.FullName
}
foreach ($name in $expected.Keys) {
    if (-not $actual.ContainsKey($name)) { throw "Pinned file $name is missing from $pinnedDir. Reinstall the signer." }
    if ($actual[$name] -ne $expected[$name]) { throw "Pinned file $name changed since it was installed. Do NOT sign; reinstall the signer from a reviewed commit." }
}
foreach ($name in $actual.Keys) {
    if (-not $expected.ContainsKey($name)) { throw "Unexpected file $name in $pinnedDir. Do NOT sign; reinstall the signer." }
}
$signer = Join-Path $pinnedDir 'RacksSigner.dll'
Write-Host "RacksSigner from $($trusted.commit) ($($trusted.trustedBy)), $($expected.Count) files match trusted.json"

# gh.exe explicitly: the personal GitHub CLI login, not a shell wrapper of the same name.
$gh = (Get-Command gh.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source

# Plain function (no param block) so "--repo"-style flags pass through $args untouched.
function Invoke-Gh {
    $out = & $gh @args
    if ($LASTEXITCODE -ne 0) { throw "gh.exe $($args -join ' ') failed (exit $LASTEXITCODE)" }
    return $out
}

function Assert-GhOwner {
    $login = (Invoke-Gh api user --jq .login | Out-String).Trim()
    if ($login -cne $Owner) { throw "gh.exe is logged in as '$login', not '$Owner'. Nothing was uploaded." }
}

Assert-GhOwner
Write-Host "gh.exe is logged in as $Owner"

# Read-only git with the tag-signing trust anchored outside the repo. No pager, no fsmonitor.
function Invoke-Git {
    $out = & git --no-pager -C $Clone -c core.fsmonitor=false -c gpg.format=ssh -c "gpg.ssh.allowedSignersFile=$allowedSigners" @args
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed (exit $LASTEXITCODE)" }
    return $out
}

function Test-SignedTag([string]$Name) {
    & git --no-pager -C $Clone -c core.fsmonitor=false -c gpg.format=ssh -c "gpg.ssh.allowedSignersFile=$allowedSigners" verify-tag $Name 2>$null | Out-Null
    return ($LASTEXITCODE -eq 0)
}

# --- 2. Signed tag: GitHub must have the same tag object ------------------------------------------
Step "Checking tag $tag"
if (-not (Test-Path -LiteralPath $allowedSigners)) { throw "No $allowedSigners. Do the one-time tag signing setup (docs/RELEASING.md)." }
& git --no-pager -C $Clone rev-parse --verify --quiet "refs/tags/$tag" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Tag $tag does not exist in $Clone. Fetch tags first (git fetch --tags)." }
if ((Invoke-Git cat-file -t "refs/tags/$tag") -ne 'tag') { throw "Tag $tag is a lightweight tag. Release tags must be signed: git tag -s $tag" }
$tagObject = (Invoke-Git rev-parse "refs/tags/$tag").Trim()
$tagHeader = @(Invoke-Git cat-file tag $tagObject | Where-Object { $_ -like 'tag *' }) | Select-Object -First 1
if ($tagHeader -cne "tag $tag") { throw "The tag object names '$tagHeader', not '$tag'." }
& git --no-pager -C $Clone -c core.fsmonitor=false -c gpg.format=ssh -c "gpg.ssh.allowedSignersFile=$allowedSigners" verify-tag $tag
if ($LASTEXITCODE -ne 0) { throw "Tag $tag is not signed with your tag signing key. Do NOT sign this release." }
$localCommit = (Invoke-Git rev-parse "refs/tags/$tag^{commit}").Trim()

$ref = Invoke-Gh api "repos/$Repo/git/ref/tags/$tag" | ConvertFrom-Json
if ($ref.object.type -ne 'tag' -or $ref.object.sha -ne $tagObject) {
    throw "Tag $tag on GitHub is $($ref.object.type) $($ref.object.sha), but your signed tag object is $tagObject. Stop and investigate."
}
$remoteCommit = (Invoke-Gh api "repos/$Repo/git/tags/$($ref.object.sha)" | ConvertFrom-Json).object.sha
if ($remoteCommit -ne $localCommit) {
    throw "Tag $tag on GitHub points at $remoteCommit, but your signed tag at $localCommit. Stop and investigate."
}
Write-Host "Tag $tag -> $localCommit, signed by your key; GitHub has the same tag object"

# --- 3. Draft release and the CI run that built it ------------------------------------------------
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

$work = Join-Path ([IO.Path]::GetTempPath()) ("racks-release-$Version-" + [guid]::NewGuid().ToString('N'))
$draftDir = Join-Path $work 'draft'
$ciDir = Join-Path $work 'ci'
$manifestPath = Join-Path $draftDir $manifestName
$sigPath = Join-Path $draftDir $sigName
$uploaded = $false
$published = $false

try {
    # --- 4. Download and cross-check --------------------------------------------------------------
    Step "Downloading the draft assets and the CI artifact"
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

    # --- 5. Review, then confirm BEFORE anything is signed ----------------------------------------
    Step "What you are about to sign"
    $previous = $null
    $candidates = @(Invoke-Git tag --list 'v*' --merged "$localCommit^" --sort=-v:refname)
    foreach ($t in $candidates) {
        if ($t -and $t -ne $tag -and (Test-SignedTag $t)) { $previous = $t; break }
    }
    if ($previous) {
        Write-Host "Changes since the last signed tag $previous :"
    } else {
        $previous = @($candidates | Where-Object { $_ -and $_ -ne $tag }) | Select-Object -First 1
        if ($previous) {
            Write-Host "WARNING: no earlier release tag is signed with your key. Review EVERYTHING since $previous :" -ForegroundColor Yellow
        }
    }
    if ($previous) {
        Invoke-Git log --oneline --no-decorate "$previous..$localCommit" | Out-Host
        Write-Host ""
        Invoke-Git diff --stat --no-ext-diff --no-textconv $previous $localCommit | Out-Host
    } else {
        Write-Host "WARNING: no earlier release tag. Review the whole history." -ForegroundColor Yellow
        Invoke-Git log --oneline --no-decorate $localCommit | Out-Host
    }
    # Same list as install-signer.ps1. '--' is quoted so PowerShell passes it on to git.
    $signerSources = @(
        'tools',
        'Racks/Util/UpdateManifest.cs',
        'Racks/Util/UpdateTrust.cs',
        ':(glob)**/Directory.Build.*',
        ':(glob)**/Directory.Packages.props',
        ':(glob)**/global.json',
        ':(glob,icase)**/nuget.config',
        ':(glob)**/.gitattributes'
    )
    $signerChanges = @(Invoke-Git diff --stat --no-ext-diff --no-textconv $trusted.commit $localCommit '--' @signerSources | Where-Object { $_ })
    if ($signerChanges.Count -gt 0) {
        Write-Host ""
        Write-Host "Note: the signer sources changed since your installed signer ($($trusted.commit)):" -ForegroundColor Yellow
        $signerChanges | Out-Host
    }

    Write-Host ""
    Write-Host "Release:   $($release.url)"
    Write-Host "Commit:    $localCommit"
    Write-Host "Installer: $installerName  sha256 $draftHash  ($((Get-Item -LiteralPath $installer).Length) bytes)"
    Write-Host ""
    $phrase = "sign and publish $tag"
    $answer = Read-Host "Type '$phrase' to sign this release and publish it to every user right away (anything else stops here, nothing is signed)"
    if ($answer -cne $phrase) {
        Write-Host "Stopped. Nothing was signed or uploaded; the draft is unchanged."
        exit 1
    }

    # --- 6. Sign, upload, publish ------------------------------------------------------------------
    Step "Signing with the pinned RacksSigner (key from Windows Credential Manager)"
    Push-Location $pinnedDir
    try {
        & dotnet $signer sign --installer $installer --version $Version
        if ($LASTEXITCODE -ne 0) { throw "Signing failed." }
        & dotnet $signer verify --installer $installer --version $Version
        if ($LASTEXITCODE -ne 0) { throw "The signed files do not verify." }
    } finally { Pop-Location }

    Step "Uploading the manifest and signature, then publishing"
    Assert-GhOwner
    $uploaded = $true
    Invoke-Gh release upload $tag $manifestPath $sigPath --repo $Repo | Out-Null

    $checkDir = Join-Path $work 'roundtrip'
    New-Item -ItemType Directory -Force $checkDir | Out-Null
    Invoke-Gh release download $tag --repo $Repo --dir $checkDir --pattern $manifestName --pattern $sigName | Out-Null
    if ((Get-Sha256 (Join-Path $checkDir $manifestName)) -ne (Get-Sha256 $manifestPath) -or
        (Get-Sha256 (Join-Path $checkDir $sigName)) -ne (Get-Sha256 $sigPath)) {
        throw "The uploaded manifest/signature differ from the local ones."
    }

    Invoke-Gh release edit $tag --repo $Repo --draft=false --latest | Out-Null
    $published = $true
} finally {
    # In finally, not catch: PowerShell runs it on an error AND when the run is stopped (Ctrl+C,
    # Ctrl+Break, closing the console), so a stopped run cannot leave a signed draft behind.
    if ($uploaded -and -not $published) {
        # The publish call may have gone through just before the stop: a published release keeps
        # its signature. Anything else (still a draft, or the state is unknown) loses it.
        $stillDraft = $true
        try {
            $view = & $gh release view $tag --repo $Repo --json isDraft
            if ($LASTEXITCODE -eq 0) {
                $isDraft = ($view | Out-String | ConvertFrom-Json).isDraft
                if ($isDraft -is [bool] -and -not $isDraft) { $stillDraft = $false }
            }
        } catch { $stillDraft = $true }
        if ($stillDraft) {
            Write-Host "Removing the signed files from the draft so no signed draft is left behind..." -ForegroundColor Yellow
            foreach ($name in @($manifestName, $sigName)) {
                & $gh release delete-asset $tag $name --repo $Repo --yes 2>$null | Out-Null
            }
            try {
                $left = @((& $gh release view $tag --repo $Repo --json assets | ConvertFrom-Json).assets |
                        ForEach-Object { $_.name } | Where-Object { $_ -eq $manifestName -or $_ -eq $sigName })
            } catch { $left = @('(could not check)') }
            if ($left.Count -gt 0) {
                Write-Host "COULD NOT REMOVE $($left -join ', ') from the draft. Delete them on the release page NOW." -ForegroundColor Red
            }
        } else {
            $published = $true
            Write-Host "The release was published before the run stopped; its signature stays." -ForegroundColor Yellow
        }
    }
    # Signed files, downloads and the round-trip copies never outlive the run.
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $work) { Write-Host "Could not delete $work. Delete it by hand." -ForegroundColor Yellow }
}

$latest = Invoke-Gh api "repos/$Repo/releases/latest" | ConvertFrom-Json
if ($latest.tag_name -ne $tag) {
    Write-Host "Published, but releases/latest reports $($latest.tag_name). Check the release page." -ForegroundColor Yellow
}
Write-Host ""
Write-Host "Published $tag. Updater clients will now offer it." -ForegroundColor Green
