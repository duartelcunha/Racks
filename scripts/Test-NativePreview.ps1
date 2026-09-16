[CmdletBinding()]
param([string]$Dotnet = 'dotnet', [string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$profile = Join-Path $repo ('.artifacts/native-preview-' + [Guid]::NewGuid().ToString('N'))
$app = Join-Path $repo "Racks/bin/$Configuration/net10.0-windows10.0.26100.0/Racks.dll"
if (-not (Test-Path -LiteralPath $app)) { throw 'Build the native Racks project first.' }
$dotnetPath = (Get-Command $Dotnet).Source
foreach ($pass in 1..2) {
    $process = Start-Process -FilePath $dotnetPath -ArgumentList @(('"' + $app + '"'), '--profile', ('"' + $profile + '"'), '--design-preview', '--native-smoke') -PassThru -WindowStyle Hidden
    if (-not $process.WaitForExit(30000)) {
        $process.Kill()
        throw "Native preview timed out on pass $pass. Evidence: $profile"
    }
    $result = Get-Content -LiteralPath (Join-Path $profile 'native-smoke.json') -Raw | ConvertFrom-Json
    if ($process.ExitCode -ne 0 -or -not $result.Passed) { throw "Native preview failed on pass ${pass}: $($result.Error)" }
    $crashLog = Join-Path $profile 'Roaming/Racks/crash.log'
    if (Test-Path -LiteralPath $crashLog) { throw "Native preview logged an unhandled exception: $crashLog" }
    Move-Item -LiteralPath (Join-Path $profile 'native-smoke.json') -Destination (Join-Path $profile "pass-$pass.json")
    Write-Host "Native preview pass ${pass}: $($result.Checks.Count) checks passed."
}
Write-Host "Native startup/restart evidence: $profile"
