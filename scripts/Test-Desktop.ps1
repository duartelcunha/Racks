[CmdletBinding()]
param([string]$Dotnet = 'dotnet', [ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$profilePath = Join-Path $repoRoot ('.artifacts/smoke-' + [Guid]::NewGuid().ToString('N'))
$assembly = Join-Path $repoRoot "src/Racks.Desktop/bin/$Configuration/net10.0/Racks.Next.dll"
if (!(Test-Path -LiteralPath $assembly)) { throw 'Build Racks.Desktop before running this check.' }
# Each run creates its own profile. Never terminate any other Racks process.
& $Dotnet $assembly --profile $profilePath --smoke-test
if ($LASTEXITCODE -ne 0) { throw "Application smoke process failed ($LASTEXITCODE). Inspect $profilePath." }
$reportPath = Join-Path $profilePath 'smoke-result.json'
if (!(Test-Path -LiteralPath $reportPath)) { throw 'Application exited without a smoke result.' }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$report | ConvertTo-Json -Depth 8
if (!$report.Passed) { throw 'Application smoke checks failed.' }
