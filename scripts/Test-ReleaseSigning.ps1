[CmdletBinding()]
param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$fixture = Join-Path $repoRoot ('.artifacts/signing-' + [guid]::NewGuid().ToString('N'))
$keys = Join-Path $fixture 'ephemeral-keys'
New-Item -ItemType Directory -Path $keys -Force | Out-Null
Push-Location $repoRoot
try {
    & $Dotnet tool restore
    if ($LASTEXITCODE) { throw 'Tool restore failed.' }
    # Test-only keys stay in the ignored, isolated fixture. Never use these in a release.
    & $Dotnet tool run netsparkle-generate-appcast -- --generate-keys --key-path $keys
    if ($LASTEXITCODE) { throw 'Test key generation failed.' }
    $publicKey = [IO.File]::ReadAllText((Join-Path $keys 'NetSparkle_Ed25519.pub')).Trim()
    foreach ($target in @(@{ Platform='windows-x64'; Extension='exe' }, @{ Platform='macos-arm64'; Extension='zip' })) {
        $package = Join-Path $fixture ('Racks-' + $target.Platform + '-2.0.0-beta.1.' + $target.Extension)
        [IO.File]::WriteAllText($package, 'Inert signature fixture. Never executed or extracted.')
        & "$PSScriptRoot/New-UpdateFeed.ps1" -PackagePath $package -Version '2.0.0-beta.1' -Platform $target.Platform -DownloadBase 'https://github.com/duartelcunha/Racks/releases/download/v2.0.0-beta.1' -PublicKey $publicKey -KeyPath $keys -OutputDirectory (Join-Path $fixture $target.Platform) -Dotnet $Dotnet
    }
    Write-Host 'Windows/Mac feed generation and verification passed with test-only keys.'
} finally { Pop-Location }
