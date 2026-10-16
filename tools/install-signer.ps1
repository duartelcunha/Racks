#Requires -Version 5.1
<#
.SYNOPSIS
    Build RacksSigner ONCE from a reviewed commit and pin it outside the repository (docs/RELEASING.md).

.DESCRIPTION
    The signing key lives on the owner's machine, so that machine must never build or run code
    straight from a working tree that a stolen GitHub token could have changed. This script is the
    only step that builds anything for signing, and it does so from one commit you trust:

      0. The running copy of this script must be tools/install-signer.ps1 at that commit.
      1. The commit must be contained in a release tag signed with your tag signing key (checked
         against %LOCALAPPDATA%\RacksSigner\allowed_signers, outside the repo), OR you review
         `git show --stat` for it and type the full commit sha back.
      2. The tree of THAT commit (git archive, not the working tree) is extracted to a temporary
         folder and RacksSigner is published to %LOCALAPPDATA%\RacksSigner\<commit>\, together with
         that commit's tools\release-sign.ps1.
      3. The SHA-256 of every installed file is recorded in %LOCALAPPDATA%\RacksSigner\trusted.json.
         The installed release-sign.ps1 refuses to run unless its folder is the trusted commit and
         every file still has its recorded hash. It never builds anything.

    This script itself comes from the repository, so never run the working-tree copy: extract it
    from the commit you install, read that copy, and run it. It refuses to run unless its own
    content is exactly tools/install-signer.ps1 at -Commit.

.EXAMPLE
    $sha = '3f2c...'                                   # full 40-character sha
    git -C C:\src\Racks show --stat $sha               # review
    git -C C:\src\Racks show "${sha}:tools/install-signer.ps1" > "$env:TEMP\install-signer-$sha.ps1"
    notepad "$env:TEMP\install-signer-$sha.ps1"        # review the copy you are about to run
    & "$env:TEMP\install-signer-$sha.ps1" -Commit $sha -Clone C:\src\Racks
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-f]{40}$')]
    [string]$Commit,

    # The clone to read the commit from.
    [Parameter(Mandatory = $true)]
    [string]$Clone
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$base = Join-Path $env:LOCALAPPDATA 'RacksSigner'
$allowedSigners = Join-Path $base 'allowed_signers'
$trustedPath = Join-Path $base 'trusted.json'
$dest = Join-Path $base $Commit
$Clone = (Resolve-Path -LiteralPath $Clone).Path

# Everything that decides what gets built and signed: the signer itself, the scripts, the shared
# manifest/key code, and every file MSBuild, the SDK, NuGet or git archive read on the way.
# release-sign.ps1 carries the same list for its "signer sources changed" note.
$SignerSources = @(
    'tools',
    'Racks/Util/UpdateManifest.cs',
    'Racks/Util/UpdateTrust.cs',
    ':(glob)**/Directory.Build.*',
    ':(glob)**/Directory.Packages.props',
    ':(glob)**/global.json',
    ':(glob,icase)**/nuget.config',
    ':(glob)**/.gitattributes'
)

# Read-only git with the tag-signing trust anchored outside the repo. No pager, no fsmonitor, no
# external diff or textconv drivers.
function Invoke-Git {
    $out = & git --no-pager -C $Clone -c core.fsmonitor=false -c gpg.format=ssh -c "gpg.ssh.allowedSignersFile=$allowedSigners" @args
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed (exit $LASTEXITCODE)" }
    return $out
}

function Test-SignedTag([string]$Tag) {
    if (-not (Test-Path -LiteralPath $allowedSigners)) { return $false }
    & git --no-pager -C $Clone -c core.fsmonitor=false -c gpg.format=ssh -c "gpg.ssh.allowedSignersFile=$allowedSigners" verify-tag $Tag 2>$null | Out-Null
    return ($LASTEXITCODE -eq 0)
}

if (Test-Path -LiteralPath $dest) {
    throw "$dest already exists. Delete that folder first if you really mean to rebuild this commit."
}
& git --no-pager -C $Clone cat-file -e "$Commit^{commit}" 2>$null
if ($LASTEXITCODE -ne 0) { throw "Commit $Commit is not in $Clone. Fetch first." }

# --- 0. Only the reviewed copy of this script may run ---------------------------------------------
# The copy that runs must be tools/install-signer.ps1 exactly as it is at -Commit, not a working
# tree file someone could have changed. Compared line by line, so a copy saved by `git show ... >`
# (UTF-16 in Windows PowerShell, UTF-8 in PowerShell 7, CRLF) still matches. The script is
# ASCII only (ReleaseScriptTests), so no decoding can make two different files look equal.
function ConvertTo-Lines([string]$Text) {
    $lines = [Collections.Generic.List[string]]($Text -split "`r?`n")
    while ($lines.Count -gt 0 -and $lines[$lines.Count - 1] -eq '') { $lines.RemoveAt($lines.Count - 1) }
    return ($lines -join "`n")
}
$committedLines = @(& git --no-pager -C $Clone -c core.fsmonitor=false cat-file blob "${Commit}:tools/install-signer.ps1")
if ($LASTEXITCODE -ne 0) { throw "Commit $Commit has no tools/install-signer.ps1." }
$committed = ConvertTo-Lines ($committedLines -join "`n")
$running = ConvertTo-Lines ([IO.File]::ReadAllText($PSCommandPath))
if ($running -cne $committed) {
    throw ("This copy of install-signer.ps1 ($PSCommandPath) is not tools/install-signer.ps1 at $Commit. Nothing was built. " +
        "Extract and review that commit's copy, then run it (docs/RELEASING.md).")
}

# --- 1. Decide whether this commit is trusted ---------------------------------------------------
$trustedBy = $null
$tags = @(Invoke-Git tag --list 'v*' --contains $Commit)
foreach ($t in $tags) {
    if ($t -and (Test-SignedTag $t)) { $trustedBy = "signed tag $t"; break }
}

if ($trustedBy) {
    Write-Host "Commit $Commit is contained in $trustedBy (verified against $allowedSigners)."
} else {
    if (-not (Test-Path -LiteralPath $allowedSigners)) {
        Write-Host "No $allowedSigners yet, so no tag can be verified (one-time setup, docs/RELEASING.md)." -ForegroundColor Yellow
    } else {
        Write-Host "No release tag signed with your key contains $Commit." -ForegroundColor Yellow
    }
    Write-Host ""
    Invoke-Git show --stat --no-ext-diff --no-textconv --format=fuller $Commit | Out-Host
    Write-Host ""
    Write-Host "Signer and build sources at this commit (review them if they changed since your last install):"
    Invoke-Git log --oneline -n 20 $Commit '--' @SignerSources | Out-Host
    $previous = $null
    if (Test-Path -LiteralPath $trustedPath) {
        try { $previous = [string](Get-Content -Raw -LiteralPath $trustedPath | ConvertFrom-Json).commit } catch { $previous = $null }
    }
    if ($previous -match '^[0-9a-f]{40}$') {
        & git --no-pager -C $Clone cat-file -e "$previous^{commit}" 2>$null
        if ($LASTEXITCODE -eq 0) {
            Write-Host ""
            Write-Host "Changed since your installed signer ($previous):"
            Invoke-Git diff --stat --no-ext-diff --no-textconv $previous $Commit '--' @SignerSources | Out-Host
        }
    }
    Write-Host ""
    $typed = Read-Host "Type the full 40-character commit sha to trust this commit (anything else cancels)"
    if ($typed.Trim() -cne $Commit) { throw "Not confirmed. Nothing was built." }
    $trustedBy = 'reviewed and typed by the owner'
}

# --- 2. Build that commit's tree, not the working tree --------------------------------------------
$work = Join-Path ([IO.Path]::GetTempPath()) ("racks-signer-build-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work | Out-Null
try {
    $zip = Join-Path $work 'src.zip'
    $src = Join-Path $work 'src'
    Invoke-Git archive --format=zip -o $zip $Commit | Out-Null
    Expand-Archive -LiteralPath $zip -DestinationPath $src

    New-Item -ItemType Directory -Force $base | Out-Null
    Push-Location $src
    try {
        & dotnet publish (Join-Path $src 'tools\RacksSigner\RacksSigner.csproj') -c Release -o $dest --self-contained false --disable-build-servers -nologo -v q | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Building RacksSigner failed." }
    } finally { Pop-Location }

    Copy-Item -LiteralPath (Join-Path $src 'tools\release-sign.ps1') -Destination (Join-Path $dest 'release-sign.ps1')
} catch {
    if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Recurse -Force -ErrorAction SilentlyContinue }
    throw
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

# --- 3. Pin the hashes outside the repo -----------------------------------------------------------
$files = [ordered]@{}
# -Force: hidden and system files are hashed too, so release-sign.ps1 flags any it did not expect.
Get-ChildItem -LiteralPath $dest -Recurse -File -Force | Sort-Object FullName | ForEach-Object {
    $rel = $_.FullName.Substring($dest.Length + 1)
    $files[$rel] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}
$record = [ordered]@{
    commit      = $Commit
    trustedBy   = $trustedBy
    installedAt = (Get-Date).ToUniversalTime().ToString('o')
    files       = $files
}
[IO.File]::WriteAllText($trustedPath, ($record | ConvertTo-Json -Depth 4), (New-Object System.Text.UTF8Encoding($false)))

Write-Host ""
Write-Host "Installed RacksSigner from $Commit ($trustedBy)" -ForegroundColor Green
Write-Host "  folder   $dest"
Write-Host "  dll      $($files['RacksSigner.dll'])"
Write-Host "  pinned   $trustedPath"
Write-Host ""
Write-Host "Sign releases with the INSTALLED script, never the repo copy:"
Write-Host "  & `"$dest\release-sign.ps1`" -Version <x.y.z> -Clone `"$Clone`""
