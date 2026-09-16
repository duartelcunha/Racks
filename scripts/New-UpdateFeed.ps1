[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackagePath,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][ValidateSet('windows-x64', 'macos-arm64')][string]$Platform,
    [Parameter(Mandatory)][uri]$DownloadBase,
    [Parameter(Mandatory)][string]$PublicKey,
    [Parameter(Mandatory)][string]$KeyPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$Dotnet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
if ($DownloadBase.Scheme -ne 'https') { throw 'Package downloads must use HTTPS.' }
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw 'Expected a semantic release version.' }
if ([Convert]::FromBase64String($PublicKey).Length -ne 32) { throw 'Expected an Ed25519 public key.' }
$package = (Resolve-Path -LiteralPath $PackagePath).Path
$keys = (Resolve-Path -LiteralPath $KeyPath).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
$publicKeyFile = Join-Path $keys 'NetSparkle_Ed25519.pub'
if (!(Test-Path -LiteralPath (Join-Path $keys 'NetSparkle_Ed25519.priv')) -or !(Test-Path -LiteralPath $publicKeyFile)) {
    throw 'Provide existing maintainer-owned NetSparkle keys. Release builds never generate keys.'
}
if ([IO.File]::ReadAllText($publicKeyFile).Trim() -ne $PublicKey.Trim()) { throw 'The signing key does not match the public key embedded in the app.' }
if (Test-Path -LiteralPath $output) {
    if (@(Get-ChildItem -LiteralPath $output -Force).Count) { throw 'Use a new or empty feed output directory.' }
}
$extension = [IO.Path]::GetExtension($package)
if (($Platform -eq 'windows-x64' -and $extension -ne '.exe') -or ($Platform -eq 'macos-arm64' -and $extension -ne '.zip')) { throw 'Unexpected package type for this platform.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    & $Dotnet tool restore
    if ($LASTEXITCODE) { throw 'Appcast generator restore failed.' }
    & $Dotnet tool run netsparkle-generate-appcast -- --single-file $package --file-extract-version true --file-version $Version --os $Platform --base-url $DownloadBase.AbsoluteUri.TrimEnd('/') --appcast-output-directory $output -n 'Racks' --key-path $keys --public-key-override $PublicKey --use-ed25519-signature-attribute
    if ($LASTEXITCODE) { throw 'Appcast generation failed.' }
    $feedPath = Join-Path $output 'appcast.xml'
    $signaturePath = $feedPath + '.signature'
    if (!(Test-Path -LiteralPath $feedPath) -or !(Test-Path -LiteralPath $signaturePath)) { throw 'Generator did not produce a signed feed.' }
    [xml]$feed = [IO.File]::ReadAllText($feedPath)
    $enclosures = @($feed.rss.channel.item.enclosure)
    if ($enclosures.Count -ne 1) { throw 'Expected one package in the generated feed.' }
    $enclosure = $enclosures[0]
    $sparkleNamespace = 'http://www.andymatuschak.org/xml-namespaces/sparkle'
    if ($enclosure.GetAttribute('version', $sparkleNamespace) -ne $Version) { throw 'Feed version differs from the package version.' }
    if ($enclosure.GetAttribute('os', $sparkleNamespace) -ne $Platform) { throw 'Feed platform differs from the package platform.' }
    $expectedUrl = $DownloadBase.AbsoluteUri.TrimEnd('/') + '/' + [uri]::EscapeDataString([IO.Path]::GetFileName($package))
    if ([uri]$enclosure.url -ne [uri]$expectedUrl) { throw 'Unexpected package URL in the generated feed.' }
    foreach ($item in @(
        @{ Path=$feedPath; Signature=[IO.File]::ReadAllText($signaturePath).Trim() },
        @{ Path=$package; Signature=$enclosure.GetAttribute('edSignature', $sparkleNamespace) }
    )) {
        if (!$item.Signature) { throw 'A required signature is missing.' }
        $verification = & $Dotnet tool run netsparkle-generate-appcast -- --verify $item.Path --signature $item.Signature --key-path $keys --public-key-override $PublicKey 2>&1
        if ($LASTEXITCODE -or ($verification -join "`n") -notmatch '(?m)^Signature valid\s*$') { throw ('Signature verification failed for ' + [IO.Path]::GetFileName($item.Path)) }
    }
    Write-Host "Verified package and feed signatures: $output"
} finally { Pop-Location }
