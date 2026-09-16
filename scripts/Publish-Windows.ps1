[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublicKey,
    [Parameter(Mandatory)][uri]$FeedBase,
    [Parameter(Mandatory)][uri]$DownloadBase,
    [Parameter(Mandatory)][string]$KeyPath,
    [string]$Dotnet = 'dotnet',
    [string]$InnoCompiler = 'ISCC.exe'
)
$ErrorActionPreference = 'Stop'
if ($FeedBase.Scheme -ne 'https') { throw 'The release feed must use HTTPS.' }
if ([Convert]::FromBase64String($PublicKey).Length -ne 32) { throw 'Expected an Ed25519 public key.' }
$repoRoot = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content -LiteralPath (Join-Path $repoRoot 'src/Racks.Desktop/Racks.Desktop.csproj') -Raw)).Project.PropertyGroup.Version
Push-Location $repoRoot
try {
    & "$PSScriptRoot/Test-All.ps1" -Dotnet $Dotnet
    & $Dotnet publish src/Racks.Desktop/Racks.Desktop.csproj -c Release -r win-x64 --self-contained true -o .artifacts/publish/win-x64 "-p:RacksUpdatePublicKey=$PublicKey" "-p:RacksUpdateFeedBase=$FeedBase" --warnaserror
    if ($LASTEXITCODE) { throw 'Publish failed.' }
    & $InnoCompiler "/DAppVersion=$version" '/DAppExeName=Racks.Next.exe' '/DSourceRoot=..\.artifacts\publish\win-x64' installer/Racks.iss
    if ($LASTEXITCODE) { throw 'Installer compilation failed.' }
    $package = Join-Path $repoRoot "installer/Output/Racks-Setup-$version.exe"
    $feedOutput = Join-Path $repoRoot ('.artifacts/release-' + [guid]::NewGuid().ToString('N') + '/win-x64')
    & "$PSScriptRoot/New-UpdateFeed.ps1" -PackagePath $package -Version $version -Platform windows-x64 -DownloadBase $DownloadBase -PublicKey $PublicKey -KeyPath $KeyPath -OutputDirectory $feedOutput -Dotnet $Dotnet
    Write-Host "Installer and signed feed verified. Upload the installer to $DownloadBase and the feed files under $FeedBase/win-x64/. No release was uploaded."
} finally { Pop-Location }
