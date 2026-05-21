# Build the distributable Racks-Setup-x.y.z.exe.
#
# Pipeline:
#   1. publish.ps1 -SelfContained  →  .\publish\  (bundled .NET 10 runtime)
#   2. iscc.exe installer\Racks.iss  →  .\installer\Output\Racks-Setup-<ver>.exe
#
# Requires Inno Setup 6 (https://jrsoftware.org/isdl.php). The script auto-
# detects ISCC.exe at the usual install locations; pass -IsccPath to override.
#
# Usage:
#   .\build-installer.ps1                 # produces Racks-Setup-<ver-from-csproj>.exe
#   .\build-installer.ps1 -SkipPublish    # reuse existing .\publish\ contents
#   .\build-installer.ps1 -IsccPath C:\Tools\InnoSetup\ISCC.exe

param(
    [switch]$SkipPublish,
    [string]$IsccPath,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

# --- Locate ISCC.exe first so we fail fast if Inno Setup isn't installed. ---
if (-not $IsccPath) {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:LocalAppData}\Programs\Inno Setup 6\ISCC.exe"
    )
    $IsccPath = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}
if (-not $IsccPath -or -not (Test-Path $IsccPath)) {
    Write-Error @"
ISCC.exe (Inno Setup 6 compiler) not found.

Install Inno Setup 6 from https://jrsoftware.org/isdl.php (free), then re-run
this script. If you installed to a non-default location, pass -IsccPath:
    .\build-installer.ps1 -IsccPath 'C:\path\to\ISCC.exe'
"@
    exit 1
}

# --- Pull the version from the csproj so the installer filename + AppVersion
#     register stay in lockstep with the assembly version. ---
$csprojPath = Join-Path $PSScriptRoot "Racks\Racks.csproj"
[xml]$csproj = Get-Content $csprojPath
$version = $csproj.Project.PropertyGroup.AssemblyVersion | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { $version = "2.0.0" }
Write-Host "Building installer for Racks $version"

# --- Step 1: produce a self-contained publish folder. ---
if (-not $SkipPublish) {
    Write-Host "[1/2] Publishing self-contained build..."
    $publishScript = Join-Path $PSScriptRoot "publish.ps1"
    & $publishScript -SelfContained -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) { Write-Error "publish.ps1 failed."; exit $LASTEXITCODE }
} else {
    if (-not (Test-Path (Join-Path $PSScriptRoot "publish\Racks.exe"))) {
        Write-Error "-SkipPublish set but .\publish\Racks.exe doesn't exist. Run without -SkipPublish first."
        exit 1
    }
    Write-Host "[1/2] Reusing existing .\publish\ (SkipPublish)."
}

# --- Step 2: compile the installer. ---
Write-Host "[2/2] Compiling installer with $IsccPath ..."
$issPath = Join-Path $PSScriptRoot "installer\Racks.iss"
& $IsccPath "/DAppVersion=$version" $issPath
if ($LASTEXITCODE -ne 0) { Write-Error "iscc failed."; exit $LASTEXITCODE }

$outExe = Join-Path $PSScriptRoot "installer\Output\Racks-Setup-$version.exe"
if (Test-Path $outExe) {
    $size = [math]::Round((Get-Item $outExe).Length / 1MB, 1)
    Write-Host ""
    Write-Host "Built $outExe ($size MB)"
    Write-Host "Distribute that single .exe — double-click it to install Racks (no UAC, no choices)."
} else {
    Write-Warning "Expected output not found at $outExe — check iscc output above."
}
