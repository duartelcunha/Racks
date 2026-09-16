[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackagePath,
    [Parameter(Mandatory)][string]$InstallPath,
    [Parameter(Mandatory)][string]$FixtureRoot,
    [string]$Dotnet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or !$IsWindows) { throw 'Update installation requires a disposable hosted Windows runner.' }
$fixture = [IO.Path]::GetFullPath($FixtureRoot)
$runnerRoot = [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('\') + '\'
if (!$fixture.StartsWith($runnerRoot, [StringComparison]::OrdinalIgnoreCase) -or ![IO.Path]::GetFullPath($InstallPath).StartsWith($fixture + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Installer fixture paths must stay inside the disposable runner directory.' }
$keys = Join-Path $fixture 'update-test-keys'
$webRoot = Join-Path $fixture 'update-server'
New-Item -ItemType Directory -Path $keys, $webRoot -Force | Out-Null
$package = Join-Path $webRoot ([IO.Path]::GetFileName($PackagePath))
Copy-Item -LiteralPath $PackagePath -Destination $package
$server = $null
$harness = $null
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    & $Dotnet tool restore
    if ($LASTEXITCODE) { throw 'Tool restore failed.' }
    & $Dotnet tool run netsparkle-generate-appcast -- --generate-keys --key-path $keys
    if ($LASTEXITCODE) { throw 'Test key generation failed.' }
    $publicKey = [IO.File]::ReadAllText((Join-Path $keys 'NetSparkle_Ed25519.pub')).Trim()
    & "$PSScriptRoot/New-UpdateFeed.ps1" -PackagePath $package -Version '2.0.0-beta.2' -Platform windows-x64 -DownloadBase 'https://github.com/duartelcunha/Racks/releases/download/v2.0.0-beta.2' -PublicKey $publicKey -KeyPath $keys -OutputDirectory (Join-Path $fixture 'verified-release-feed') -Dotnet $Dotnet
    $serverScript = Join-Path $fixture 'serve-update.py'
    @'
import functools, http.server, pathlib, sys
handler = functools.partial(http.server.SimpleHTTPRequestHandler, directory=sys.argv[1])
server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), handler)
pathlib.Path(sys.argv[2]).write_text(str(server.server_port))
server.serve_forever()
'@ | Set-Content -LiteralPath $serverScript
    $portPath = Join-Path $fixture 'update-port.txt'
    $server = Start-Process -FilePath 'python' -ArgumentList @(('"'+$serverScript+'"'), ('"'+$webRoot+'"'), ('"'+$portPath+'"')) -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $fixture 'update-server.log')
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while (!(Test-Path -LiteralPath $portPath)) {
        if ($server.HasExited -or [DateTime]::UtcNow -gt $deadline) { throw 'Local update server failed to start.' }
        Start-Sleep -Milliseconds 100
    }
    $port = [int][IO.File]::ReadAllText($portPath)
    $baseUrl = "http://127.0.0.1:$port"
    # HTTP is restricted to this loopback fixture. Production feed generation requires HTTPS.
    & $Dotnet tool run netsparkle-generate-appcast -- --single-file $package --file-extract-version true --file-version '2.0.0-beta.2' --os windows-x64 --base-url $baseUrl --appcast-output-directory $webRoot -n Racks --key-path $keys --use-ed25519-signature-attribute
    if ($LASTEXITCODE) { throw 'Fixture feed generation failed.' }
    & $Dotnet build tests/Racks.UpdateHarness -c Release --warnaserror
    if ($LASTEXITCODE) { throw 'Update harness build failed.' }
    $profile = Join-Path $fixture 'update-profile'
    $resultPath = Join-Path $fixture 'update-result.txt'
    $logPath = Join-Path $fixture 'upgrade.log'
    $installerArguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /DIR="' + $InstallPath + '" /LOG="' + $logPath + '"'
    # ProcessStartInfo.ArgumentList preserves nested installer argument quoting.
    $info = [Diagnostics.ProcessStartInfo]::new($Dotnet)
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true; $info.WindowStyle = 'Hidden'
    foreach ($argument in @('tests/Racks.UpdateHarness/bin/Release/net10.0/Racks.UpdateHarness.dll', $profile, "$baseUrl/appcast.xml", $publicKey, $installerArguments, $resultPath, $InstallPath)) { $info.ArgumentList.Add($argument) }
    $harness = [Diagnostics.Process]::Start($info)
    if (!$harness.WaitForExit(120000)) { throw 'Signed update did not request shutdown within two minutes.' }
    if ($harness.ExitCode -ne 0 -or !(Test-Path -LiteralPath $resultPath) -or [IO.File]::ReadAllText($resultPath) -notlike 'Graceful shutdown*') {
        $detail = if (Test-Path -LiteralPath $resultPath) { [IO.File]::ReadAllText($resultPath) } else { 'No shutdown result.' }
        throw "Update harness failed: $detail"
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    while ($true) {
        if (Test-Path -LiteralPath $logPath) {
            try {
                $log = [IO.File]::ReadAllText($logPath)
                if ($log -match 'Installation process succeeded\.' -and $log -match 'Log closed\.') { break }
            } catch [IO.IOException] { }
        }
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Updater exited but the installation did not finish successfully.' }
        Start-Sleep -Milliseconds 500
    }
    $smokePath = Join-Path $profile 'smoke-result.json'
    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    while (!(Test-Path -LiteralPath $smokePath)) {
        if ([DateTime]::UtcNow -gt $deadline) { throw 'The installed application did not relaunch and complete its isolated smoke test.' }
        Start-Sleep -Milliseconds 500
    }
    # The smoke report is atomically replaced before the process exits normally.
    $smoke = Get-Content -LiteralPath $smokePath -Raw | ConvertFrom-Json
    Copy-Item -LiteralPath $smokePath -Destination .artifacts/update-relaunch-smoke.json
    if (!$smoke.Passed) { throw 'The relaunched installed application failed its smoke test.' }
    $installedExecutable = Join-Path $InstallPath 'Racks.Next.exe'
    foreach ($process in @(Get-Process -Name Racks.Next -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $installedExecutable })) {
        if (!$process.WaitForExit(15000)) { throw 'The isolated relaunched application did not exit normally.' }
    }
    $restartInfo = [Diagnostics.ProcessStartInfo]::new($installedExecutable)
    $restartInfo.UseShellExecute = $false; $restartInfo.CreateNoWindow = $true; $restartInfo.WindowStyle = 'Hidden'
    foreach ($argument in @('--profile', $profile, '--smoke-restart-check')) { $restartInfo.ArgumentList.Add($argument) }
    $restartProcess = [Diagnostics.Process]::Start($restartInfo)
    try {
        if (!$restartProcess.WaitForExit(120000) -or $restartProcess.ExitCode -ne 0) { throw 'Installed application restart verification failed.' }
    } finally {
        if (!$restartProcess.HasExited) { $restartProcess.Kill(); $restartProcess.WaitForExit() }
        $restartProcess.Dispose()
    }
    $restartPath = Join-Path $profile 'restart-result.json'
    $restart = Get-Content -LiteralPath $restartPath -Raw | ConvertFrom-Json
    Copy-Item -LiteralPath $restartPath -Destination .artifacts/update-relaunch-restart.json
    if (!$restart.Passed) { throw 'Installed application did not preserve its isolated workspace and undo.' }
    Write-Host 'Actual updater verified the feed/package, shut down gracefully, installed, and relaunched the app; workspace and undo survived restart.'
} finally {
    if ($harness -and !$harness.HasExited) { $harness.Kill(); $harness.WaitForExit() }
    if ($server -and !$server.HasExited) { $server.Kill(); $server.WaitForExit() }
    Pop-Location
}
