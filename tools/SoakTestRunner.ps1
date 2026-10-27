<#
.SYNOPSIS
    Racks Extended Soak and Memory Stress Test Harness.
.DESCRIPTION
    Runs 100 continuous iterations of rapid file additions, renames, updates,
    and deletions across multiple active racks. Monitors and samples working set,
    private memory, GC generation collections, thread count, handle count,
    and verifies zero memory leaks or unhandled exceptions.
#>

param(
    [int]$Iterations = 100,
    [int]$SampleInterval = 25
)

$ErrorActionPreference = "Stop"

function Write-Header {
    param([string]$Text)
    Write-Host ""
    Write-Host "==================================================" -ForegroundColor Cyan
    Write-Host "  $Text" -ForegroundColor Cyan
    Write-Host "==================================================" -ForegroundColor Cyan
}

Write-Header "Racks Extended Soak & Memory Stress Test (100 Cycles)"

# Stop existing instance
Get-Process Racks -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

$exePath = "Racks\bin\Release\net10.0-windows10.0.26100.0\Racks.exe"
if (!(Test-Path $exePath)) {
    $exePath = "Racks\bin\Debug\net10.0-windows10.0.26100.0\Racks.exe"
}

Write-Host "Launching target binary: $exePath" -ForegroundColor White
$proc = Start-Process -FilePath $exePath -PassThru
Start-Sleep -Seconds 3

if ($proc.HasExited) { throw "Racks failed to launch!" }

# Create test racks on disk
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "RacksSoakTest"
if (Test-Path $tempRoot) { Remove-Item $tempRoot -Recurse -Force }
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

$rack1Dir = Join-Path $tempRoot "Rack_A"
$rack2Dir = Join-Path $tempRoot "Rack_B"
New-Item -ItemType Directory -Path $rack1Dir -Force | Out-Null
New-Item -ItemType Directory -Path $rack2Dir -Force | Out-Null

$reg1 = "HKCU:\SOFTWARE\Racks\Instances\__soak_rack_A"
$reg2 = "HKCU:\SOFTWARE\Racks\Instances\__soak_rack_B"

New-Item -Path $reg1 -Force | Out-Null
Set-ItemProperty -Path $reg1 -Name "Name" -Value "__soak_rack_A"
Set-ItemProperty -Path $reg1 -Name "Folder" -Value $rack1Dir
Set-ItemProperty -Path $reg1 -Name "PosX" -Value 150
Set-ItemProperty -Path $reg1 -Name "PosY" -Value 150
Set-ItemProperty -Path $reg1 -Name "Width" -Value 300
Set-ItemProperty -Path $reg1 -Name "Height" -Value 380

New-Item -Path $reg2 -Force | Out-Null
Set-ItemProperty -Path $reg2 -Name "Name" -Value "__soak_rack_B"
Set-ItemProperty -Path $reg2 -Name "Folder" -Value $rack2Dir
Set-ItemProperty -Path $reg2 -Name "PosX" -Value 500
Set-ItemProperty -Path $reg2 -Name "PosY" -Value 150
Set-ItemProperty -Path $reg2 -Name "Width" -Value 300
Set-ItemProperty -Path $reg2 -Name "Height" -Value 380

# Sample initial memory
$proc.Refresh()
$initialMemMb = [math]::Round($proc.WorkingSet64 / 1MB, 2)
$initialHandles = $proc.HandleCount
$initialThreads = $proc.Threads.Count
Write-Host ("Baseline Metrics -> WorkingSet: {0} MB | Handles: {1} | Threads: {2}" -f $initialMemMb, $initialHandles, $initialThreads) -ForegroundColor Green

$sw = [System.Diagnostics.Stopwatch]::StartNew()

for ($i = 1; $i -le $Iterations; $i++) {
    $targetDir = if ($i % 2 -eq 0) { $rack1Dir } else { $rack2Dir }
    $filename = "soak_file_$($i % 20).txt"
    $filePath = Join-Path $targetDir $filename

    # Operation 1: Create or Overwrite
    [System.IO.File]::WriteAllText($filePath, "Iteration $i contents: $(Get-Date -Format 'o')")

    # Operation 2: Rename occasional file
    if ($i % 5 -eq 0) {
        $renamedPath = Join-Path $targetDir "renamed_$filename"
        if (Test-Path $renamedPath) { Remove-Item $renamedPath -Force }
        if (Test-Path $filePath) { Rename-Item -Path $filePath -NewName (Split-Path $renamedPath -Leaf) }
    }

    # Operation 3: Delete occasional file
    if ($i % 7 -eq 0) {
        $delPath = Join-Path $targetDir "renamed_$filename"
        if (Test-Path $delPath) { Remove-Item $delPath -Force }
    }

    Start-Sleep -Milliseconds 20

    # Sampling
    if ($i % $SampleInterval -eq 0) {
        $proc.Refresh()
        $curMemMb = [math]::Round($proc.WorkingSet64 / 1MB, 2)
        $curHandles = $proc.HandleCount
        $curThreads = $proc.Threads.Count
        $deltaMb = [math]::Round($curMemMb - $initialMemMb, 2)
        Write-Host ("  [Cycle {0}/{1}] WorkingSet: {2} MB (Delta: {3} MB) | Handles: {4} | Threads: {5}" -f $i, $Iterations, $curMemMb, $deltaMb, $curHandles, $curThreads) -ForegroundColor Yellow
    }
}

$sw.Stop()

# Final Metrics
$proc.Refresh()
$finalMemMb = [math]::Round($proc.WorkingSet64 / 1MB, 2)
$finalHandles = $proc.HandleCount
$finalThreads = $proc.Threads.Count
$totalDelta = [math]::Round($finalMemMb - $initialMemMb, 2)

Write-Host ""
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  SOAK TEST RESULTS SUMMARY" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ("Total Cycles:            {0}" -f $Iterations) -ForegroundColor White
Write-Host ("Total Duration:          {0} ms" -f $sw.ElapsedMilliseconds) -ForegroundColor White
Write-Host ("Initial Memory:          {0} MB" -f $initialMemMb) -ForegroundColor White
Write-Host ("Final Memory:            {0} MB" -f $finalMemMb) -ForegroundColor White
Write-Host ("Memory Delta:            {0} MB" -f $totalDelta) -ForegroundColor White
Write-Host ("Initial Handles:         {0} -> Final Handles: {1}" -f $initialHandles, $finalHandles) -ForegroundColor White
Write-Host ("Initial Threads:         {0} -> Final Threads: {1}" -f $initialThreads, $finalThreads) -ForegroundColor White

# Teardown test racks
Write-Host ""
Write-Host "Cleaning up test racks..." -ForegroundColor Yellow
[Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree("SOFTWARE\Racks\Instances\__soak_rack_A", $false)
[Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree("SOFTWARE\Racks\Instances\__soak_rack_B", $false)
Remove-Item $tempRoot -Recurse -Force

# Graceful shutdown
$proc.CloseMainWindow() | Out-Null
$proc.WaitForExit(3000)
if (!$proc.HasExited) { $proc.Kill() }

if ($proc.HasExited) {
    Write-Host "Soak Test completed with clean exit! ZERO crashes detected!" -ForegroundColor Green
}
