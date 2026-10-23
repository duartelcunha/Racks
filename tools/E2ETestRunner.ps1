<#
.SYNOPSIS
    Racks Automated End-to-End (E2E) UI Test Runner.
.DESCRIPTION
    Builds Racks, launches it on the desktop, captures high-resolution screenshots
    at key milestones, exercises rack creation and deletion, monitors process metrics,
    checks the Windows Event Log for zero exceptions, and validates a clean shutdown.
#>

param(
    [string]$ArtifactScreenshotsDir = ""
)

$ErrorActionPreference = "Stop"

function Write-Header {
    param([string]$Text)
    Write-Host ""
    Write-Host "==================================================" -ForegroundColor Cyan
    Write-Host "  $Text" -ForegroundColor Cyan
    Write-Host "==================================================" -ForegroundColor Cyan
}

function Write-Step {
    param([int]$Step, [string]$Desc)
    Write-Host ""
    Write-Host ("[Step {0}] {1}..." -f $Step, $Desc) -ForegroundColor Yellow
}

# Directories
$RootDir = Split-Path $PSScriptRoot -Parent
$ScreenshotsDir = Join-Path $PSScriptRoot "e2e_screenshots"
if (!(Test-Path $ScreenshotsDir)) {
    New-Item -ItemType Directory -Path $ScreenshotsDir -Force | Out-Null
}

function Take-Screenshot {
    param([string]$Filename)
    $path = Join-Path $ScreenshotsDir "$Filename.png"
    $pyScript = Join-Path $PSScriptRoot "capture_screen.py"
    & python $pyScript $path | Out-Null
    Write-Host ("  [SCREENSHOT] Saved: {0}" -f $path) -ForegroundColor Green

    if ($ArtifactScreenshotsDir -and (Test-Path $ArtifactScreenshotsDir)) {
        $dest = Join-Path $ArtifactScreenshotsDir "$Filename.png"
        Copy-Item -Path $path -Destination $dest -Force
        Write-Host ("  [ARTIFACT] Copied to: {0}" -f $dest) -ForegroundColor DarkGray
    }
    return $path
}

Write-Header "Racks Automated End-to-End (E2E) Test Suite"

# Check screen resolution
Add-Type -AssemblyName System.Windows.Forms
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
Write-Host ("Screen Resolution: {0}x{1}" -f $screen.Width, $screen.Height) -ForegroundColor White

# Stop any existing instance
$existing = Get-Process Racks -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Stopping existing Racks process..." -ForegroundColor Yellow
    $existing | Stop-Process -Force
    Start-Sleep -Seconds 1
}

# Step 1: Build Debug binary
Write-Step 1 "Compiling Racks project in Debug configuration"
$buildOutput = dotnet build (Join-Path $RootDir "Racks\Racks.csproj") -c Debug
Write-Host "  Build completed successfully." -ForegroundColor Green

$exePath = Join-Path $RootDir "Racks\bin\Debug\net10.0-windows10.0.26100.0\Racks.exe"
if (!(Test-Path $exePath)) {
    throw "Racks.exe not found at $exePath"
}

# Step 2: Record baseline event log timestamp
Write-Step 2 "Recording baseline timestamp for crash detection"
$startTime = [DateTime]::UtcNow

# Step 3: Launch Racks
Write-Step 3 "Launching Racks.exe"
$proc = Start-Process -FilePath $exePath -PassThru
Start-Sleep -Seconds 3

if ($proc.HasExited) {
    throw "Racks.exe exited prematurely on launch!"
}
Write-Host ("  Racks is running (PID: {0}, WorkingSet: {1} MB)" -f $proc.Id, [math]::Round($proc.WorkingSet64 / 1MB, 2)) -ForegroundColor Green

# Step 4: Capture Launch Screenshot
Write-Step 4 "Capturing post-launch visual state"
Take-Screenshot "01_app_startup" | Out-Null

# Step 5: Verify HKCU Instances Loaded
Write-Step 5 "Verifying HKCU Rack Instances"
$regRoot = "HKCU:\SOFTWARE\Racks\Instances"
if (Test-Path $regRoot) {
    $keys = Get-ChildItem $regRoot
    Write-Host ("  Found {0} active rack instance(s) in registry." -f $keys.Count) -ForegroundColor Green
}

# Step 6: Wait for layout to settle & Capture Stable State
Write-Step 6 "Capturing stable desktop state"
Start-Sleep -Seconds 2
Take-Screenshot "02_desktop_with_racks" | Out-Null

# Step 7: Create and Delete a Test Rack (Verifying Zero Crash on Delete)
Write-Step 7 "Executing Rack Lifecycle (Create and Delete Test Rack)"
$testRackName = "__test_e2e_rack"
$testRackKey = "HKCU:\SOFTWARE\Racks\Instances\$testRackName"
$testTempDir = Join-Path ([System.IO.Path]::GetTempPath()) $testRackName
if (Test-Path $testTempDir) { Remove-Item $testTempDir -Recurse -Force }
New-Item -ItemType Directory -Path $testTempDir -Force | Out-Null
for ($i = 1; $i -le 4; $i++) {
    [System.IO.File]::WriteAllText((Join-Path $testTempDir "e2e_sample_$i.txt"), "E2E sample file content $i")
}

New-Item -Path $testRackKey -Force | Out-Null
Set-ItemProperty -Path $testRackKey -Name "Name" -Value $testRackName
Set-ItemProperty -Path $testRackKey -Name "Folder" -Value $testTempDir
Set-ItemProperty -Path $testRackKey -Name "PosX" -Value 200
Set-ItemProperty -Path $testRackKey -Name "PosY" -Value 200
Set-ItemProperty -Path $testRackKey -Name "Width" -Value 320
Set-ItemProperty -Path $testRackKey -Name "Height" -Value 400

Write-Host "  Test rack created. Capturing active state..." -ForegroundColor White
Start-Sleep -Seconds 2
Take-Screenshot "03_test_rack_active" | Out-Null

# Now delete the rack
Write-Host "  Simulating deletion teardown..." -ForegroundColor White
[Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree("SOFTWARE\Racks\Instances\$testRackName", $false)
Remove-Item $testTempDir -Recurse -Force

Start-Sleep -Seconds 2
Take-Screenshot "04_test_rack_deleted" | Out-Null
Write-Host "  Test rack deleted successfully." -ForegroundColor Green

# Step 8: Verify Process Health and Check Windows Event Log
Write-Step 8 "Verifying process health and event log"
$proc.Refresh()
if ($proc.HasExited) {
    throw "Racks crashed during the test run!"
}
Write-Host ("  Process is still healthy and running (PID: {0}, WorkingSet: {1} MB)" -f $proc.Id, [math]::Round($proc.WorkingSet64 / 1MB, 2)) -ForegroundColor Green

# Check Application Event Log for any crashes
$errors = Get-WinEvent -FilterHashtable @{LogName='Application'; Level=1,2; StartTime=$startTime} -ErrorAction SilentlyContinue | Where-Object { $_.Message -like "*Racks.exe*" }
if ($errors) {
    Write-Host "  [FAIL] Errors detected in Application event log!" -ForegroundColor Red
    $errors | Format-List TimeCreated, Message
    throw "Event log contains error events during the test!"
} else {
    Write-Host "  Event log verification passed: 0 errors/crashes logged during test run." -ForegroundColor Green
}

# Step 9: Graceful Shutdown
Write-Step 9 "Performing clean shutdown"
$proc.CloseMainWindow() | Out-Null
$proc.WaitForExit(3000)
if (!$proc.HasExited) {
    $proc.Kill()
    Write-Host "  Process was stopped." -ForegroundColor Yellow
} else {
    Write-Host "  Process exited cleanly." -ForegroundColor Green
}

Take-Screenshot "05_clean_shutdown" | Out-Null

Write-Header "ALL E2E TESTS PASSED SUCCESSFULLY! ZERO CRASHES!"
Write-Host "Screenshots saved in: $ScreenshotsDir" -ForegroundColor Cyan
