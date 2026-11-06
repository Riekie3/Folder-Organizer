<#
.SYNOPSIS
    Idle performance, working set, and resource leak benchmark script (Phase 8).
    Samples CPU utilization, Working Set (MB), Private Bytes (MB), Handle count,
    and Win32 GDI/USER object counts via GetGuiResources over time.

.PARAMETER DurationSeconds
    Total benchmark measurement duration in seconds (default: 30).

.PARAMETER IntervalSeconds
    Sampling interval in seconds (default: 2).
#>

[CmdletBinding()]
param(
    [int]$DurationSeconds = 30,
    [int]$IntervalSeconds = 2
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " GlassFolders Performance & Stability Benchmark (Phase 8)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$proc = Get-Process -Name "GlassFolders" -ErrorAction SilentlyContinue
if (-not $proc) {
    Write-Warning "GlassFolders is not running. Starting Release binary..."
    $exePath = Join-Path $PSScriptRoot "..\..\src\GlassFolders.App\bin\Release\net10.0-windows\GlassFolders.exe"
    if (-not (Test-Path $exePath)) {
        throw "Could not find GlassFolders.exe at $exePath."
    }
    Start-Process -FilePath $exePath
    Start-Sleep -Seconds 2
    $proc = Get-Process -Name "GlassFolders" -ErrorAction Stop
}

Write-Host "Target PID: $($proc.Id) ($($proc.ProcessName))" -ForegroundColor Green

# P/Invoke for GetGuiResources
$nativeDef = @"
using System;
using System.Runtime.InteropServices;

public static class GuiResourceProbe
{
    [DllImport("user32.dll")]
    public static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);

    public const uint GR_GDIOBJECTS = 0;
    public const uint GR_USEROBJECTS = 1;
}
"@

if (-not ([System.Management.Automation.PSTypeName]'GuiResourceProbe').Type) {
    Add-Type -TypeDefinition $nativeDef
}

$hProcess = $proc.Handle
$initialGdi = [GuiResourceProbe]::GetGuiResources($hProcess, 0)
$initialUser = [GuiResourceProbe]::GetGuiResources($hProcess, 1)

Write-Host "Baseline resources: GDI Objects: $initialGdi, USER Objects: $initialUser, Handles: $($proc.Handles)" -ForegroundColor Yellow
Write-Host "`nMeasuring idle utilization over $DurationSeconds seconds (sampling every ${IntervalSeconds}s)...`n" -ForegroundColor Cyan

$samples = [System.Collections.Generic.List[PSObject]]::new()
$startTime = [System.Diagnostics.Stopwatch]::StartNew()
$lastCpuTime = $proc.TotalProcessorTime
$lastWallTime = [DateTime]::UtcNow

while ($startTime.Elapsed.TotalSeconds -lt $DurationSeconds) {
    Start-Sleep -Seconds $IntervalSeconds

    $proc.Refresh()
    $nowWall = [DateTime]::UtcNow
    $nowCpu = $proc.TotalProcessorTime

    $wallDelta = ($nowWall - $lastWallTime).TotalMilliseconds
    $cpuDelta = ($nowCpu - $lastCpuTime).TotalMilliseconds
    $lastWallTime = $nowWall
    $lastCpuTime = $nowCpu

    $cpuPercent = 0.0
    if ($wallDelta -gt 0) {
        $cpuPercent = [Math]::Round(($cpuDelta / ($wallDelta * [Environment]::ProcessorCount)) * 100, 2)
    }

    $wsMb = [Math]::Round($proc.WorkingSet64 / 1MB, 1)
    $privMb = [Math]::Round($proc.PrivateMemorySize64 / 1MB, 1)
    $handles = $proc.Handles
    $gdi = [GuiResourceProbe]::GetGuiResources($hProcess, 0)
    $user = [GuiResourceProbe]::GetGuiResources($hProcess, 1)

    $sample = [PSCustomObject]@{
        ElapsedSec  = [Math]::Round($startTime.Elapsed.TotalSeconds, 1)
        CpuPercent  = $cpuPercent
        WorkingSetMb = $wsMb
        PrivateMb   = $privMb
        Handles     = $handles
        GdiObjects  = $gdi
        UserObjects = $user
    }
    $samples.Add($sample)

    Write-Host ("[{0,4:N1}s] CPU: {1,5:N2}% | WS: {2,5:N1} MB | Priv: {3,5:N1} MB | Handles: {4,4} | GDI: {5,3} | USER: {6,3}" -f `
        $sample.ElapsedSec, $sample.CpuPercent, $sample.WorkingSetMb, $sample.PrivateMb, $sample.Handles, $sample.GdiObjects, $sample.UserObjects)
}

# Summary calculations
$avgCpu = [Math]::Round(($samples | Measure-Object -Property CpuPercent -Average).Average, 2)
$avgWs = [Math]::Round(($samples | Measure-Object -Property WorkingSetMb -Average).Average, 1)
$peakWs = ($samples | Measure-Object -Property WorkingSetMb -Maximum).Maximum
$finalSample = $samples[-1]
$gdiDelta = $finalSample.GdiObjects - $initialGdi
$userDelta = $finalSample.UserObjects - $initialUser
$handleDelta = $finalSample.Handles - $proc.Handles

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " BENCHMARK SUMMARY                                        " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Average Idle CPU:       $avgCpu% (Target: <= 0.5%)" -ForegroundColor $(if ($avgCpu -le 0.5) { "Green" } else { "Yellow" })
Write-Host " Average Working Set:    $avgWs MB (Target: < 100 MB)" -ForegroundColor $(if ($avgWs -lt 100) { "Green" } else { "Yellow" })
Write-Host " Peak Working Set:       $peakWs MB" -ForegroundColor Green
Write-Host " GDI Object Drift:       $gdiDelta (Target: 0)" -ForegroundColor $(if ($gdiDelta -eq 0) { "Green" } else { "Red" })
Write-Host " USER Object Drift:      $userDelta (Target: 0)" -ForegroundColor $(if ($userDelta -eq 0) { "Green" } else { "Red" })
Write-Host " Handle Drift:           $handleDelta (Target: ~0)" -ForegroundColor $(if ([Math]::Abs($handleDelta) -le 5) { "Green" } else { "Yellow" })
Write-Host "==========================================================" -ForegroundColor Cyan
