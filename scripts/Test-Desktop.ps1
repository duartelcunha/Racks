[CmdletBinding()]
param([string]$Dotnet = 'dotnet', [ValidateSet('Debug','Release')][string]$Configuration = 'Release', [string]$AssemblyPath)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$profilePath = Join-Path $repoRoot ('.artifacts/smoke-' + [Guid]::NewGuid().ToString('N'))
$assembly = if ($AssemblyPath) { [IO.Path]::GetFullPath($AssemblyPath) } else { Join-Path $repoRoot "src/Racks.Desktop/bin/$Configuration/net10.0/Racks.Next.dll" }
if (!(Test-Path -LiteralPath $assembly)) { throw 'Build Racks.Desktop before running this check.' }
# Each run creates its own profile. Never terminate any other Racks process.
& $Dotnet $assembly --profile $profilePath --smoke-test
if ($LASTEXITCODE -ne 0) { throw "Application smoke process failed ($LASTEXITCODE). Inspect $profilePath." }
$reportPath = Join-Path $profilePath 'smoke-result.json'
if (!(Test-Path -LiteralPath $reportPath)) { throw 'Application exited without a smoke result.' }
$report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
$report | ConvertTo-Json -Depth 8
if (!$report.Passed) { throw 'Application smoke checks failed.' }
& $Dotnet $assembly --profile $profilePath --smoke-restart-check
if ($LASTEXITCODE -ne 0) { throw 'Application restart verification failed.' }
$restart = Get-Content -LiteralPath (Join-Path $profilePath 'restart-result.json') -Raw | ConvertFrom-Json
$restart | ConvertTo-Json -Depth 8
if (!$restart.Passed) { throw 'Restart checks failed.' }
