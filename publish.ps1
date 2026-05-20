# Builds a portable single-folder Release of Racks.exe.
#
# Usage from a PowerShell prompt at the repo root:
#   .\publish.ps1            -> framework-dependent build (needs .NET 10 runtime)
#   .\publish.ps1 -SelfContained   -> bundles the .NET runtime into the output
#   .\publish.ps1 -SingleFile      -> packs everything into one .exe (still needs WPF runtime files in same dir)
#
# Output ends up in:
#   .\publish\Racks.exe   (+ supporting files)

param(
    [switch]$SelfContained,
    [switch]$SingleFile,
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutDir = "publish"
)

$ErrorActionPreference = "Stop"

$projectPath = Join-Path $PSScriptRoot "Racks\Racks.csproj"
$outFull = Join-Path $PSScriptRoot $OutDir

if (Test-Path $outFull) {
    Write-Host "Cleaning $outFull..."
    Remove-Item -Recurse -Force $outFull
}

$publishArgs = @(
    "publish", $projectPath,
    "-c", $Configuration,
    "-r", $Runtime,
    "-o", $outFull
)

if ($SelfContained) {
    $publishArgs += "--self-contained", "true"
} else {
    $publishArgs += "--self-contained", "false"
}

# MSBuild properties need to be a single token per arg (use -p:, not /p:, to play
# nicely with PowerShell's arg parser).
if ($SingleFile) {
    $publishArgs += "-p:PublishSingleFile=true"
    $publishArgs += "-p:IncludeNativeLibrariesForSelfExtract=true"
}

Write-Host "Running: dotnet $($publishArgs -join ' ')"
& dotnet @publishArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

$exe = Join-Path $outFull "Racks.exe"
if (Test-Path $exe) {
    $size = (Get-Item $exe).Length
    Write-Host ""
    Write-Host "Built $exe ($([math]::Round($size / 1MB, 1)) MB)"
    Write-Host "Run it from anywhere — the folder is portable."
} else {
    Write-Warning "Racks.exe not found in $outFull. Check the dotnet output above."
}
