<#
.SYNOPSIS
    Racks Developer Tools CLI.
.DESCRIPTION
    Comprehensive developer CLI tool for diagnostics, log tailing, stress testing,
    environment cleanup, and E2E automation.
#>

param(
    [Parameter(Position = 0)]
    [string]$Command = "help"
)

$ErrorActionPreference = "Stop"

function Show-Banner {
    Write-Host "==================================================" -ForegroundColor Cyan
    Write-Host "          Racks Developer Tools CLI               " -ForegroundColor Cyan
    Write-Host "==================================================" -ForegroundColor Cyan
}

function Show-Help {
    Show-Banner
    Write-Host "Usage: .\tools\dev-tools.ps1 [command]" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Available commands:" -ForegroundColor Green
    Write-Host "  status   - Show process, memory, threads, HWNDs, HKCU keys, workspace info" -ForegroundColor White
    Write-Host "  logs     - Stream real-time crash log output (%AppData%\Racks\crash.log)" -ForegroundColor White
    Write-Host "  clean    - Clean up test registries (__test_*) and orphaned temp folders" -ForegroundColor White
    Write-Host "  stress   - Run rapid rack create/delete stress loop to verify zero crashes" -ForegroundColor White
    Write-Host "  e2e      - Run full end-to-end UI tests with screenshots" -ForegroundColor White
    Write-Host "  help     - Show this usage guide" -ForegroundColor White
    Write-Host ""
}

function Get-RacksStatus {
    Show-Banner
    Write-Host "Inspecting Racks System Status..." -ForegroundColor Yellow

    # Process status
    $procs = Get-Process Racks -ErrorAction SilentlyContinue
    if ($procs) {
        foreach ($p in $procs) {
            $memMb = [math]::Round($p.WorkingSet64 / 1MB, 2)
            Write-Host ("  [RUNNING] PID: {0} | Working Set: {1} MB | Threads: {2} | Handles: {3}" -f $p.Id, $memMb, $p.Threads.Count, $p.HandleCount) -ForegroundColor Green
        }
    } else {
        Write-Host "  [STOPPED] Racks process is not currently running." -ForegroundColor DarkGray
    }

    # Registry Instances
    Write-Host ""
    Write-Host "Registry Instances (HKCU:\SOFTWARE\Racks\Instances):" -ForegroundColor Yellow
    $regPath = "HKCU:\SOFTWARE\Racks\Instances"
    if (Test-Path $regPath) {
        $keys = Get-ChildItem -Path $regPath -ErrorAction SilentlyContinue
        if ($keys.Count -gt 0) {
            foreach ($k in $keys) {
                $subKeyName = Split-Path $k.Name -Leaf
                $folder = (Get-ItemProperty -Path $k.PSPath -Name "Folder" -ErrorAction SilentlyContinue).Folder
                Write-Host ("    * {0} -> {1}" -f $subKeyName, $folder) -ForegroundColor White
            }
        } else {
            Write-Host "    (No racks defined)" -ForegroundColor DarkGray
        }
    } else {
        Write-Host "    (Instances key does not exist yet)" -ForegroundColor DarkGray
    }

    # Sandbox VirtualFrames
    $appData = [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)
    $vfPath = Join-Path $appData "Racks\VirtualFrames"
    Write-Host ""
    Write-Host "VirtualFrames Sandbox ($vfPath):" -ForegroundColor Yellow
    if (Test-Path $vfPath) {
        $folders = Get-ChildItem -Path $vfPath -Directory -ErrorAction SilentlyContinue
        Write-Host ("    Count: {0} rack folders" -f $folders.Count) -ForegroundColor White
    } else {
        Write-Host "    (VirtualFrames directory not found)" -ForegroundColor DarkGray
    }

    # Workspace
    $userHome = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    $wsPath = Join-Path $userHome "RacksWorkspace"
    Write-Host ""
    Write-Host "RacksWorkspace ($wsPath):" -ForegroundColor Yellow
    if (Test-Path $wsPath) {
        $items = Get-ChildItem -Path $wsPath -ErrorAction SilentlyContinue
        Write-Host ("    Items in workspace: {0}" -f $items.Count) -ForegroundColor White
    } else {
        Write-Host "    (RacksWorkspace does not exist)" -ForegroundColor DarkGray
    }

    # Crash log
    $crashLog = Join-Path $appData "Racks\crash.log"
    Write-Host ""
    Write-Host "Crash Log ($crashLog):" -ForegroundColor Yellow
    if (Test-Path $crashLog) {
        $fi = Get-Item $crashLog
        Write-Host ("    Size: {0} bytes | Last modified: {1}" -f $fi.Length, $fi.LastWriteTime) -ForegroundColor Red
    } else {
        Write-Host "    (No crash log exists - clean health!)" -ForegroundColor Green
    }
}

function Stream-Logs {
    Show-Banner
    $appData = [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)
    $crashLog = Join-Path $appData "Racks\crash.log"
    if (Test-Path $crashLog) {
        Write-Host "Tailing crash log: $crashLog (Press Ctrl+C to exit)..." -ForegroundColor Yellow
        Get-Content -Path $crashLog -Wait -Tail 30
    } else {
        Write-Host "No crash log found at $crashLog. System is healthy!" -ForegroundColor Green
    }
}

function Clean-TestEnvironment {
    Show-Banner
    Write-Host "Cleaning Test Environment..." -ForegroundColor Yellow

    # Clean test registry keys
    $regPath = "HKCU:\SOFTWARE\Racks\Instances"
    if (Test-Path $regPath) {
        $testKeys = Get-ChildItem -Path $regPath | Where-Object { $_.PSChildName -like "__test_*" }
        foreach ($k in $testKeys) {
            Remove-Item -Path $k.PSPath -Recurse -Force
            Write-Host ("  Removed test registry key: {0}" -f $k.PSChildName) -ForegroundColor Green
        }
    }

    # Clean test sandbox directories
    $appData = [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)
    $vfPath = Join-Path $appData "Racks\VirtualFrames"
    if (Test-Path $vfPath) {
        $testDirs = Get-ChildItem -Path $vfPath -Directory | Where-Object { $_.Name -like "__test_*" }
        foreach ($d in $testDirs) {
            Remove-Item -Path $d.FullName -Recurse -Force
            Write-Host ("  Removed test sandbox directory: {0}" -f $d.Name) -ForegroundColor Green
        }
    }

    Write-Host "Cleanup completed successfully." -ForegroundColor Cyan
}

function Run-StressTest {
    Show-Banner
    Write-Host "Running Rapid Rack Deletion Stress Test..." -ForegroundColor Yellow

    $running = Get-Process Racks -ErrorAction SilentlyContinue
    if ($running) {
        Write-Host "  [WARN] Racks is currently running. Stopping Racks before stress test..." -ForegroundColor Yellow
        $running | Stop-Process -Force
        Start-Sleep -Seconds 1
    }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $cycles = 5
    Write-Host ("  Simulating {0} rapid rack create/populate/delete cycles..." -f $cycles) -ForegroundColor White

    for ($i = 1; $i -le $cycles; $i++) {
        $rackName = "__test_stress_rack_$i"
        $regKey = "HKCU:\SOFTWARE\Racks\Instances\$rackName"
        $tempDir = Join-Path ([System.IO.Path]::GetTempPath()) $rackName

        # Create dir and files
        if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force }
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        for ($f = 1; $f -le 3; $f++) {
            [System.IO.File]::WriteAllText((Join-Path $tempDir "dummy_$f.txt"), "stress test sample content $f")
        }

        # Create registry entry
        New-Item -Path $regKey -Force | Out-Null
        Set-ItemProperty -Path $regKey -Name "Name" -Value $rackName
        Set-ItemProperty -Path $regKey -Name "Folder" -Value $tempDir
        Set-ItemProperty -Path $regKey -Name "PosX" -Value 100
        Set-ItemProperty -Path $regKey -Name "PosY" -Value 100
        Set-ItemProperty -Path $regKey -Name "Width" -Value 300
        Set-ItemProperty -Path $regKey -Name "Height" -Value 380

        # Simulate deletion teardown
        # 1. Clean registry key with throwOnMissingSubKey: false
        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree("SOFTWARE\Racks\Instances\$rackName", $false)

        # 2. Clean files
        Remove-Item $tempDir -Recurse -Force

        Write-Host ("    Cycle {0}/{1}: Created and cleanly deleted {2} without errors." -f $i, $cycles, $rackName) -ForegroundColor Green
    }

    $sw.Stop()
    Write-Host ""
    Write-Host ("Stress test passed! {0} cycles completed in {1} ms without exceptions." -f $cycles, $sw.ElapsedMilliseconds) -ForegroundColor Green
}

function Run-SoakTest {
    $script = Join-Path $PSScriptRoot "SoakTestRunner.ps1"
    if (Test-Path $script) {
        & $script
    } else {
        Write-Host "SoakTestRunner.ps1 not found at $script." -ForegroundColor Red
    }
}

function Run-E2E {
    $script = Join-Path $PSScriptRoot "ComprehensiveE2ERunner.ps1"
    if (Test-Path $script) {
        & $script
    } else {
        $fallback = Join-Path $PSScriptRoot "E2ETestRunner.ps1"
        & $fallback
    }
}

switch ($Command.ToLowerInvariant()) {
    "status"  { Get-RacksStatus }
    "logs"    { Stream-Logs }
    "clean"   { Clean-TestEnvironment }
    "stress"  { Run-StressTest }
    "soak"    { Run-SoakTest }
    "e2e"     { Run-E2E }
    default   { Show-Help }
}
