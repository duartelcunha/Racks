[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublicKey,
    [Parameter(Mandatory)][uri]$FeedBase,
    [string]$Dotnet = 'dotnet',
    [string]$InnoCompiler = 'ISCC.exe'
)
$ErrorActionPreference = 'Stop'
if ($FeedBase.Scheme -ne 'https') { throw 'The release feed must use HTTPS.' }
if ([Convert]::FromBase64String($PublicKey).Length -ne 32) { throw 'Expected an Ed25519 public key.' }
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
try {
    & "$PSScriptRoot/Test-All.ps1" -Dotnet $Dotnet
    & $Dotnet publish src/Racks.Desktop/Racks.Desktop.csproj -c Release -r win-x64 --self-contained true -o .artifacts/publish/win-x64 "-p:RacksUpdatePublicKey=$PublicKey" "-p:RacksUpdateFeedBase=$FeedBase" --warnaserror
    if ($LASTEXITCODE) { throw 'Publish failed.' }
    & $InnoCompiler '/DAppVersion=2.0.0-beta.1' '/DAppExeName=Racks.Next.exe' '/DSourceRoot=..\.artifacts\publish\win-x64' installer/Racks.iss
    if ($LASTEXITCODE) { throw 'Installer compilation failed.' }
    Write-Host 'Installer built. Sign it and validate the signed appcast before beta distribution. No release was uploaded.'
} finally { Pop-Location }
