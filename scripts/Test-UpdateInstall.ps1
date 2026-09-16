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
    foreach ($argument in @('tests/Racks.UpdateHarness/bin/Release/net10.0/Racks.UpdateHarness.dll', $profile, "$baseUrl/appcast.xml", $publicKey, $installerArguments, $resultPath)) { $info.ArgumentList.Add($argument) }
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
    Write-Host 'Actual updater verified the feed/package, requested graceful shutdown, and installed the signed fixture.'
} finally {
    if ($harness -and !$harness.HasExited) { $harness.Kill(); $harness.WaitForExit() }
    if ($server -and !$server.HasExited) { $server.Kill(); $server.WaitForExit() }
    Pop-Location
}
