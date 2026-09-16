[CmdletBinding()]
param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    & $Dotnet build Racks.Overhaul.sln -c Release --warnaserror
    if ($LASTEXITCODE) { throw 'Shared build failed.' }
    & $Dotnet test tests/Racks.Core.Tests -c Release --no-build --logger 'trx;LogFileName=core.trx'
    if ($LASTEXITCODE) { throw 'Core tests failed.' }
    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
        & $Dotnet test tests/Racks.Windows.Tests -c Release --logger 'trx;LogFileName=windows.trx' --warnaserror
        if ($LASTEXITCODE) { throw 'Windows regression tests failed.' }
    }
    & "$PSScriptRoot/Test-Desktop.ps1" -Dotnet $Dotnet -Configuration Release
} finally { Pop-Location }
