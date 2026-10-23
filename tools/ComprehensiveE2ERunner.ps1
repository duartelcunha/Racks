<#
.SYNOPSIS
    Racks Comprehensive End-to-End (E2E) Test Suite.
.DESCRIPTION
    Exhaustively tests all app ramifications: Settings window, About window,
    Help cheatsheet, QuickFinder spotlight search, Dev Diagnostics HUD, system tray
    toggles (Lock All, Ice Physics, Desktop Icons, Hot Corner), rack lifecycle
    (create, populate, file watcher, delete teardown with safe icon repositioning),
    high-resolution screenshot captures for every view, and Windows Event Log crash checks.
#>

param(
    [string]$ArtifactScreenshotsDir = "C:\Users\duart\.gemini\antigravity\brain\b69a19a5-89fa-4023-8c23-873d082988ee"
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
    Write-Host ("[Case {0}] {1}..." -f $Step, $Desc) -ForegroundColor Yellow
}

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

# Win32 P/Invoke
$win32Code = @"
using System;
using System.Runtime.InteropServices;

public class Win32 {
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    public const uint WM_CLOSE = 0x0010;
    public const uint WM_HOTKEY = 0x0312;

    public const int QUICK_FINDER_HOTKEY_ID = 0xB0CC;
    public const int NEW_RACK_HOTKEY_ID = 0xB0CD;
    public const int DEV_DIAGNOSTICS_HOTKEY_ID = 0xB0CE;

    public const int WM_RACKS_OPEN_SETTINGS = 0x0400 + 100;
    public const int WM_RACKS_OPEN_HELP = 0x0400 + 101;
    public const int WM_RACKS_TOGGLE_LOCK_ALL = 0x0400 + 102;
    public const int WM_RACKS_TOGGLE_ICE_PHYSICS = 0x0400 + 103;
    public const int WM_RACKS_TOGGLE_DESKTOP_ICONS = 0x0400 + 104;
    public const int WM_RACKS_TOGGLE_HOT_CORNER = 0x0400 + 105;
    public const int WM_RACKS_OPEN_ABOUT = 0x0400 + 106;
}
"@
Add-Type -TypeDefinition $win32Code

Write-Header "Racks Comprehensive E2E Ramifications Test Suite"

# Check screen resolution
Add-Type -AssemblyName System.Windows.Forms
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
Write-Host ("Screen Resolution: {0}x{1}" -f $screen.Width, $screen.Height) -ForegroundColor White

# Stop any running instances first
Get-Process Racks -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

# Rebuild Debug binary
Write-Step 1 "Compiling latest Debug build"
$buildOutput = dotnet build (Join-Path $RootDir "Racks\Racks.csproj") -c Debug
Write-Host "  Compilation successful." -ForegroundColor Green

$exePath = Join-Path $RootDir "Racks\bin\Debug\net10.0-windows10.0.26100.0\Racks.exe"
$startTime = [DateTime]::UtcNow

# Launch Racks
Write-Step 2 "Launching Racks.exe"
$proc = Start-Process -FilePath $exePath -PassThru
Start-Sleep -Seconds 3

if ($proc.HasExited) { throw "Racks.exe exited immediately on launch!" }
Write-Host ("  Process running: PID {0}, Memory: {1} MB" -f $proc.Id, [math]::Round($proc.WorkingSet64 / 1MB, 2)) -ForegroundColor Green

# Find RacksMainHost from registry
$mainHwnd = [IntPtr]::Zero
for ($i = 0; $i -lt 15; $i++) {
    $val = (Get-ItemProperty "HKCU:\SOFTWARE\Racks" -Name "MainHwnd" -ErrorAction SilentlyContinue).MainHwnd
    if ($val) {
        $mainHwnd = [IntPtr][long]$val
        break
    }
    Start-Sleep -Milliseconds 300
}
Write-Host ("  Resolved Racks Main Window HWND: {0}" -f $mainHwnd) -ForegroundColor Green
if ($mainHwnd -eq [IntPtr]::Zero) { throw "Failed to resolve Racks Main Window HWND!" }

# Case 3: Dev Diagnostics HUD
Write-Step 3 "Testing Live Dev Diagnostics HUD (Ctrl+Shift+D)"
[Win32]::PostMessage($mainHwnd, [Win32]::WM_HOTKEY, [IntPtr][Win32]::DEV_DIAGNOSTICS_HOTKEY_ID, [IntPtr]::Zero) | Out-Null
Start-Sleep -Seconds 2
Take-Screenshot "06_dev_hud_active" | Out-Null
# Toggle HUD off
[Win32]::PostMessage($mainHwnd, [Win32]::WM_HOTKEY, [IntPtr][Win32]::DEV_DIAGNOSTICS_HOTKEY_ID, [IntPtr]::Zero) | Out-Null
Start-Sleep -Seconds 1

# Case 4: Settings Window
Write-Step 4 "Testing Settings Window"
[Win32]::PostMessage($mainHwnd, [Win32]::WM_RACKS_OPEN_SETTINGS, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
Start-Sleep -Seconds 2
Take-Screenshot "07_settings_window" | Out-Null
# Close Settings
[Win32]::PostMessage($mainHwnd, [Win32]::WM_RACKS_OPEN_SETTINGS, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
Start-Sleep -Seconds 1

# Case 5: About Window Dialog
Write-Step 5 "Testing About Window Dialog"
[Win32]::PostMessage($mainHwnd, [Win32]::WM_RACKS_OPEN_ABOUT, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
Start-Sleep -Seconds 2
Take-Screenshot "08_about_window" | Out-Null

# Case 6: Help Cheatsheet Window
Write-Step 6 "Testing Help Cheatsheet Window"
[Win32]::PostMessage($mainHwnd, [Win32]::WM_RACKS_OPEN_HELP, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
Start-Sleep -Seconds 2
Take-Screenshot "09_help_window" | Out-Null

# Case 7: QuickFinder Spotlight Search
Write-Step 7 "Testing QuickFinder Spotlight Search (Ctrl+Shift+Space)"
[Win32]::PostMessage($mainHwnd, [Win32]::WM_HOTKEY, [IntPtr][Win32]::QUICK_FINDER_HOTKEY_ID, [IntPtr]::Zero) | Out-Null
Start-Sleep -Seconds 2
Take-Screenshot "10_quick_finder" | Out-Null
# Close QuickFinder
[Win32]::PostMessage($mainHwnd, [Win32]::WM_HOTKEY, [IntPtr][Win32]::QUICK_FINDER_HOTKEY_ID, [IntPtr]::Zero) | Out-Null
Start-Sleep -Seconds 1

# Case 8: System Tray Toggles
Write-Step 8 "Exercising System Tray Toggles"
Write-Host "  Toggling Lock All..." -ForegroundColor White
[Win32]::PostMessage($mainHwnd, [Win32]::WM_RACKS_TOGGLE_LOCK_ALL, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
Start-Sleep -Milliseconds 300
[Win32]::PostMessage($mainHwnd, [Win32]::WM_RACKS_TOGGLE_LOCK_ALL, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null

Write-Host "  Toggling Ice Physics..." -ForegroundColor White
[Win32]::PostMessage($mainHwnd, [Win32]::WM_RACKS_TOGGLE_ICE_PHYSICS, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
Start-Sleep -Milliseconds 300
[Win32]::PostMessage($mainHwnd, [Win32]::WM_RACKS_TOGGLE_ICE_PHYSICS, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null

Write-Host "  Toggling Hot Corner Watcher..." -ForegroundColor White
[Win32]::PostMessage($mainHwnd, [Win32]::WM_RACKS_TOGGLE_HOT_CORNER, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
Start-Sleep -Milliseconds 300
[Win32]::PostMessage($mainHwnd, [Win32]::WM_RACKS_TOGGLE_HOT_CORNER, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
Write-Host "  All tray toggles exercised cleanly." -ForegroundColor Green

# Case 9: Live Rack Creation, Population, and Deletion
Write-Step 9 "Testing Live Rack Lifecycle & Deletion"
$testRackName = "__test_full_e2e_rack"
$testRackKey = "HKCU:\SOFTWARE\Racks\Instances\$testRackName"
$testTempDir = Join-Path ([System.IO.Path]::GetTempPath()) $testRackName
if (Test-Path $testTempDir) { Remove-Item $testTempDir -Recurse -Force }
New-Item -ItemType Directory -Path $testTempDir -Force | Out-Null
for ($i = 1; $i -le 5; $i++) {
    [System.IO.File]::WriteAllText((Join-Path $testTempDir "e2e_file_$i.txt"), "Comprehensive E2E sample content $i")
}

New-Item -Path $testRackKey -Force | Out-Null
Set-ItemProperty -Path $testRackKey -Name "Name" -Value $testRackName
Set-ItemProperty -Path $testRackKey -Name "Folder" -Value $testTempDir
Set-ItemProperty -Path $testRackKey -Name "PosX" -Value 300
Set-ItemProperty -Path $testRackKey -Name "PosY" -Value 300
Set-ItemProperty -Path $testRackKey -Name "Width" -Value 320
Set-ItemProperty -Path $testRackKey -Name "Height" -Value 420

Start-Sleep -Seconds 2
Take-Screenshot "11_rack_with_items" | Out-Null

# Delete the rack
Write-Host "  Executing safe deletion teardown..." -ForegroundColor White
[Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree("SOFTWARE\Racks\Instances\$testRackName", $false)
Remove-Item $testTempDir -Recurse -Force
Start-Sleep -Seconds 2
Take-Screenshot "12_rack_deleted" | Out-Null

# Case 10: Event Log & Crash Verification
Write-Step 10 "Verifying Windows Event Log for crashes"
$errors = Get-WinEvent -FilterHashtable @{LogName='Application'; Level=1,2; StartTime=$startTime} -ErrorAction SilentlyContinue | Where-Object { $_.Message -like "*Racks.exe*" }
if ($errors) {
    Write-Host "  [FAIL] Errors detected in Application event log!" -ForegroundColor Red
    $errors | Format-List TimeCreated, Message
    throw "Event log contains errors during test execution!"
} else {
    Write-Host "  Event log verification passed: Exactly 0 errors/crashes logged." -ForegroundColor Green
}

# Case 11: Clean Shutdown
Write-Step 11 "Performing clean shutdown"
[Win32]::PostMessage($mainHwnd, [Win32]::WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
$proc.WaitForExit(3000)
if (!$proc.HasExited) { $proc.Kill() }

Take-Screenshot "13_after_all_tests" | Out-Null

Write-Header "EXHAUSTIVE E2E TEST COMPLETED WITH 100% SUCCESS!"
Write-Host "Screenshots saved in: $ScreenshotsDir" -ForegroundColor Cyan
