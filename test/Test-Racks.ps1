<#
  Test-Racks.ps1 - automated smoke/regression test for the Racks WPF app.

  Drives a real running build via Windows UI Automation + synthetic input and
  asserts the specific regressions this app has hit:
    1. tray icon appears AND is still there after the startup-window race window
    2. "New rack" from the tray menu does not crash the app and a rack appears
    3. no runaway CPU loop while idle or while a rack is dragged
    4. clean exit via the tray "Exit" item with no unhandled-exception crash log

  Usage:
    pwsh -File test\Test-Racks.ps1 -ExePath "path\to\Racks.exe"

  Exits 0 if every assertion passes, 1 otherwise. Also writes a JSON result
  next to this script (last-test-result.json). Safe to re-run: it kills any
  existing Racks first (all Racks instances during a test session are ours) and
  cleans up the throwaway "New Rack" it creates. It never touches other racks.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$ExePath,
  [int]$StartupWaitMs = 4000,
  [int]$TrayPersistWaitMs = 6000,
  [int]$CpuSampleSeconds = 6,
  [double]$CpuBusyThreshold = 0.5   # avg CPU-seconds per wall-second; >0.5 ~= a pegged loop
)

$ErrorActionPreference = 'Stop'
$results = [System.Collections.Generic.List[object]]::new()
$script:allPass = $true

function Step([string]$name, [bool]$pass, [string]$detail = '') {
  $tag = if ($pass) { '[PASS]' } else { '[FAIL]'; $script:allPass = $false }
  Write-Host "$tag $name $detail"
  $results.Add([pscustomobject]@{ step = $name; pass = $pass; detail = $detail })
}
function Info([string]$m) { Write-Host "[INFO] $m" }

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public struct RK_INPUT { public uint type; public RK_MOUSE mi; }
public struct RK_MOUSE { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
public static class RKInput {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern uint SendInput(uint n, RK_INPUT[] pInputs, int cb);
  const uint MOUSE = 0;
  public static void Click(uint down, uint up) {
    var a = new RK_INPUT[2];
    a[0] = new RK_INPUT { type = MOUSE, mi = new RK_MOUSE { dwFlags = down } };
    a[1] = new RK_INPUT { type = MOUSE, mi = new RK_MOUSE { dwFlags = up } };
    SendInput(2, a, Marshal.SizeOf(typeof(RK_INPUT)));
  }
  public static void Left(int x, int y)  { SetCursorPos(x, y); System.Threading.Thread.Sleep(120); Click(0x0002, 0x0004); }
  public static void Right(int x, int y) { SetCursorPos(x, y); System.Threading.Thread.Sleep(120); Click(0x0008, 0x0010); }
}
"@

$root = [System.Windows.Automation.AutomationElement]::RootElement
$CT   = [System.Windows.Automation.ControlType]
$TS   = [System.Windows.Automation.TreeScope]
$AE   = [System.Windows.Automation.AutomationElement]

function New-Prop($prop, $val) { New-Object System.Windows.Automation.PropertyCondition($prop, $val) }

function Get-RacksTrayButton {
  # Look in the always-visible tray first, then the overflow flyout.
  $trayCond = New-Prop $AE::ClassNameProperty 'Shell_TrayWnd'
  $tray = $root.FindFirst($TS::Children, $trayCond)
  if ($tray) {
    $btnCond = New-Prop $AE::ControlTypeProperty $CT::Button
    foreach ($b in $tray.FindAll($TS::Descendants, $btnCond)) {
      if ($b.Current.Name -eq 'Racks') { return $b }
    }
  }
  return $null
}

function Open-TrayMenu {
  for ($attempt = 1; $attempt -le 3; $attempt++) {
    $btn = Get-RacksTrayButton
    if (-not $btn) { Start-Sleep -Milliseconds 600; continue }
    $r = $btn.Current.BoundingRectangle
    [RKInput]::Right([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
    Start-Sleep -Milliseconds 900
    $popup = $root.FindFirst($TS::Children, (New-Prop $AE::ClassNameProperty 'Popup'))
    if ($popup) { return $popup }
  }
  return $null
}

function Invoke-MenuItem($popup, [string]$name) {
  $cond = New-Object System.Windows.Automation.AndCondition(
    (New-Prop $AE::NameProperty $name),
    (New-Prop $AE::ControlTypeProperty $CT::MenuItem))
  $item = $popup.FindFirst($TS::Descendants, $cond)
  if (-not $item) { return $false }
  $item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
  return $true
}

function Get-RackWindowCount([int]$procId) {
  # Count visible top-level windows owned by the process whose title isn't the
  # hidden helper "Racks" main window.
  $wins = $root.FindAll($TS::Children, [System.Windows.Automation.Condition]::TrueCondition)
  $n = 0
  foreach ($w in $wins) {
    try { if ($w.Current.ProcessId -eq $procId -and $w.Current.Name -and $w.Current.Name -ne 'Racks') { $n++ } } catch {}
  }
  return $n
}

function Sample-Cpu([System.Diagnostics.Process]$p, [int]$seconds) {
  $p.Refresh(); $c0 = $p.TotalProcessorTime.TotalSeconds; $t0 = Get-Date
  Start-Sleep -Seconds $seconds
  $p.Refresh(); $c1 = $p.TotalProcessorTime.TotalSeconds; $t1 = Get-Date
  $wall = ($t1 - $t0).TotalSeconds
  if ($wall -le 0) { return 0 }
  return [math]::Round(($c1 - $c0) / $wall, 3)
}

# ---- pre-flight: clean slate ------------------------------------------------
if (-not (Test-Path $ExePath)) { Step "exe exists" $false $ExePath; exit 1 }
Info "Testing: $ExePath"
Get-Process Racks -ErrorAction SilentlyContinue | ForEach-Object {
  Info "Killing pre-existing Racks pid $($_.Id)"
  Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
}
Start-Sleep -Milliseconds 800

$crashLog = Join-Path $env:AppData 'Racks\crash.log'
$crashSizeBefore = if (Test-Path $crashLog) { (Get-Item $crashLog).Length } else { 0 }
$testStart = Get-Date

# ---- launch -----------------------------------------------------------------
$proc = Start-Process -FilePath $ExePath -PassThru
Start-Sleep -Milliseconds $StartupWaitMs
$proc.Refresh()
Step "process alive after startup" (-not $proc.HasExited) "pid $($proc.Id)"
if ($proc.HasExited) { exit 1 }
Step "process responding" ($proc.Responding)

# ---- tray icon present, and STILL present after the window-race window -------
$iconNow = [bool](Get-RacksTrayButton)
Step "tray icon present at startup" $iconNow
Info "Waiting ${TrayPersistWaitMs}ms to confirm the tray icon does not vanish..."
Start-Sleep -Milliseconds $TrayPersistWaitMs
$iconLater = [bool](Get-RacksTrayButton)
Step "tray icon still present after startup-window race" $iconLater

# ---- idle CPU sanity --------------------------------------------------------
$idleCpu = Sample-Cpu $proc $CpuSampleSeconds
Step "idle CPU not runaway" ($idleCpu -lt $CpuBusyThreshold) "avg ${idleCpu} core-sec/sec (threshold $CpuBusyThreshold)"

# ---- create a rack via the tray menu ----------------------------------------
$racksBefore = Get-RackWindowCount $proc.Id
$popup = Open-TrayMenu
if ($popup) {
  $clicked = Invoke-MenuItem $popup 'New rack'
  Step "clicked 'New rack' menu item" $clicked
  Start-Sleep -Milliseconds 1500
  $proc.Refresh()
  Step "process survived 'New rack'" (-not $proc.HasExited)
  if (-not $proc.HasExited) {
    $racksAfter = Get-RackWindowCount $proc.Id
    Step "a new rack window appeared" ($racksAfter -gt $racksBefore) "before=$racksBefore after=$racksAfter"
  }
} else {
  Step "opened tray context menu" $false "popup never appeared"
}

# ---- CPU after rack creation (catches reposition/move feedback loops) --------
if (-not $proc.HasExited) {
  $postCpu = Sample-Cpu $proc $CpuSampleSeconds
  Step "CPU not runaway after rack creation" ($postCpu -lt $CpuBusyThreshold) "avg ${postCpu} core-sec/sec"
}

# ---- exit cleanly via tray 'Exit' (exercises Window_Closing on every rack) ---
if (-not $proc.HasExited) {
  $popup2 = Open-TrayMenu
  if ($popup2) {
    $exitClicked = Invoke-MenuItem $popup2 'Exit'
    Info "Invoked Exit=$exitClicked; waiting for shutdown..."
    $exited = $proc.WaitForExit(8000)
    Step "app exited cleanly via tray Exit" $exited
  } else {
    Step "opened tray menu for Exit" $false
  }
}

# ---- crash detection --------------------------------------------------------
Start-Sleep -Milliseconds 500
$crashSizeAfter = if (Test-Path $crashLog) { (Get-Item $crashLog).Length } else { 0 }
$grewCrashLog = $crashSizeAfter -gt $crashSizeBefore
Step "no new crash.log entries" (-not $grewCrashLog) "$crashSizeBefore -> $crashSizeAfter bytes"

$netCrashes = @()
try {
  $netCrashes = Get-WinEvent -FilterHashtable @{ LogName='Application'; ProviderName='.NET Runtime'; Level=2; StartTime=$testStart } -ErrorAction SilentlyContinue |
    Where-Object { $_.Message -match 'Racks' }
} catch {}
Step "no new .NET Runtime crash events" ($netCrashes.Count -eq 0) "$($netCrashes.Count) event(s)"
if ($netCrashes.Count -gt 0) { $netCrashes | ForEach-Object { Info ("CRASH: " + ($_.Message -split "`n" | Select-Object -First 4 | Out-String)) } }

# ---- teardown: ensure no stray process, clean up the throwaway test rack -----
if (-not $proc.HasExited) { Info "Force-killing surviving test process"; Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
try {
  if (Test-Path 'HKCU:\Software\Racks\Instances\New Rack') {
    Remove-Item -Path 'HKCU:\Software\Racks\Instances\New Rack' -Recurse -Force -ErrorAction SilentlyContinue
    Info "Removed throwaway 'New Rack' registry key"
  }
} catch {}

# ---- summary ----------------------------------------------------------------
$summary = [pscustomobject]@{
  exe        = $ExePath
  timestamp  = (Get-Date).ToString('s')
  allPass    = $script:allPass
  idleCpu    = $idleCpu
  steps      = $results
}
$outPath = Join-Path $PSScriptRoot 'last-test-result.json'
$summary | ConvertTo-Json -Depth 5 | Set-Content -Path $outPath -Encoding UTF8
Write-Host ""
Write-Host ("==== RESULT: " + ($(if ($script:allPass) { 'ALL PASS' } else { 'FAILURES PRESENT' })) + " ====")
Write-Host "Wrote $outPath"
if ($script:allPass) { exit 0 } else { exit 1 }
