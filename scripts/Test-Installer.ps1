[CmdletBinding()]
param([switch]$DisposableRunner, [string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
# This script intentionally exercises the real per-user installation and registry.
# It may only run on a disposable GitHub-hosted Windows VM, never a normal account.
if (!$DisposableRunner -or $env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or !$IsWindows) {
    throw 'Installer lifecycle verification requires -DisposableRunner on a GitHub-hosted Windows runner.'
}
$repoRoot = Split-Path $PSScriptRoot -Parent
$outputRoot = Join-Path $env:RUNNER_TEMP ('racks-installer-' + [Guid]::NewGuid().ToString('N'))
$installPath = Join-Path $outputRoot 'installed'
$registryPath = 'HKCU:\Software\Racks'
$fixtureFolders = @((Join-Path $env:APPDATA 'Racks'), (Join-Path $env:LOCALAPPDATA 'RacksData'), (Join-Path $env:USERPROFILE 'RacksWorkspace'))
if ((Test-Path -LiteralPath $registryPath) -or ($fixtureFolders | Where-Object { Test-Path -LiteralPath $_ })) { throw 'Runner already contains Racks data; refusing to use it.' }
$compiler = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (!$compiler) { throw 'The runner must provide Inno Setup 6.' }
New-Item -ItemType Directory -Path $outputRoot | Out-Null
$published = Invoke-RestMethod 'https://api.github.com/repos/duartelcunha/Racks/releases/tags/v1.1.4'
$asset = $published.assets | Where-Object name -eq 'Racks-Setup-1.1.4.exe' | Select-Object -First 1
if (!$asset) { throw 'The expected published upgrade baseline is unavailable.' }
$baseline = Join-Path $outputRoot $asset.name
Invoke-WebRequest $asset.browser_download_url -OutFile $baseline
if ($asset.digest -and $asset.digest.StartsWith('sha256:')) {
    if ((Get-FileHash -LiteralPath $baseline -Algorithm SHA256).Hash.ToLowerInvariant() -ne $asset.digest.Substring(7)) { throw 'Published installer digest mismatch.' }
} else { throw 'Published installer has no GitHub SHA-256 digest to verify.' }
function Run-Installer([string]$Path, [string]$LogName, [switch]$Uninstall) {
    $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /LOG="' + (Join-Path $outputRoot $LogName) + '"'
    if (!$Uninstall) { $arguments += ' /DIR="' + $installPath + '"' }
    $process = Start-Process -FilePath $Path -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(120000)) { throw 'Installer did not finish within two minutes.' }
    if ($process.ExitCode -ne 0) { throw "Installer exited with $($process.ExitCode). See $LogName." }
}
Run-Installer $baseline 'baseline.log'
foreach ($folder in $fixtureFolders) { New-Item -ItemType Directory -Path $folder -Force | Out-Null; Set-Content -LiteralPath (Join-Path $folder 'preserve-me.txt') -Value 'Racks installer preservation fixture' }
$legacyFolder = Join-Path $fixtureFolders[0] 'VirtualFrames\Installer fixture'
New-Item -ItemType Directory -Path $legacyFolder -Force | Out-Null
Set-Content -LiteralPath (Join-Path $legacyFolder 'user-file.txt') -Value 'Existing rack file'
$rackKey = Join-Path $registryPath 'Instances\Installer fixture'
New-Item -Path $rackKey -Force | Out-Null
Set-ItemProperty -LiteralPath $rackKey -Name Folder -Value $legacyFolder
$startupPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Set-ItemProperty -LiteralPath $startupPath -Name Racks -Value 'Existing startup preference'
Push-Location $repoRoot
try {
    $publishPath = Join-Path $outputRoot 'publish'
    & $Dotnet publish src/Racks.Desktop/Racks.Desktop.csproj -c Release -r win-x64 --self-contained true -o $publishPath --warnaserror
    if ($LASTEXITCODE) { throw 'Installer test publish failed.' }
    & $compiler '/DAppVersion=2.0.0-beta.1' '/DAppExeName=Racks.Next.exe' "/DSourceRoot=$publishPath" installer/Racks.iss
    if ($LASTEXITCODE) { throw 'Installer compilation failed.' }
    $installer = Join-Path $repoRoot 'installer/Output/Racks-Setup-2.0.0-beta.1.exe'
    Run-Installer $installer 'upgrade.log'
    if ((Get-ItemPropertyValue -LiteralPath $startupPath -Name Racks) -ne 'Existing startup preference') { throw 'Upgrade changed the startup preference.' }
    if (!(Test-Path -LiteralPath (Join-Path $installPath 'Racks.Next.exe'))) { throw 'Upgrade did not install the shared app.' }
    if (Test-Path -LiteralPath (Join-Path $installPath 'Racks.exe')) { throw 'Upgrade left the obsolete executable available.' }
    function Assert-Preserved {
        foreach ($folder in $fixtureFolders) { if ((Get-Content -LiteralPath (Join-Path $folder 'preserve-me.txt') -Raw).Trim() -ne 'Racks installer preservation fixture') { throw 'Installer lost user data.' } }
        if ((Get-Content -LiteralPath (Join-Path $legacyFolder 'user-file.txt') -Raw).Trim() -ne 'Existing rack file') { throw 'Installer lost an existing rack file.' }
        if ((Get-ItemPropertyValue -LiteralPath $rackKey -Name Folder) -ne $legacyFolder) { throw 'Installer lost existing settings.' }
    }
    Assert-Preserved
    Run-Installer (Join-Path $installPath 'unins000.exe') 'uninstall.log' -Uninstall
    Assert-Preserved
    if (Test-Path -LiteralPath (Join-Path $installPath 'Racks.Next.exe')) { throw 'Uninstall left the application executable installed.' }
    Run-Installer $installer 'reinstall.log'
    Assert-Preserved
    @{ Passed = $true; Baseline = 'v1.1.4'; Checks = @('Published installer upgraded', 'Startup preference preserved during upgrade', 'Files and registry survived uninstall', 'Reinstall retained files and settings') } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $repoRoot '.artifacts/installer-result.json')
} finally {
    $logs = Join-Path $repoRoot '.artifacts/installer-logs'
    New-Item -ItemType Directory -Path $logs -Force | Out-Null
    Get-ChildItem -LiteralPath $outputRoot -Filter '*.log' | Copy-Item -Destination $logs
    Pop-Location
}
