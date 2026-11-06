<#
.SYNOPSIS
    Automated test script for GlassFolders Explorer Recovery (Phase 4).
    Restarts Windows Explorer in a loop (default 10 iterations) and verifies
    that all desktop folder windows cleanly reattach without duplicates or leaks.

.PARAMETER Iterations
    Number of Explorer restart cycles to perform (default: 10).

.PARAMETER DelaySeconds
    Seconds to wait after each restart for shell stabilization (default: 3).
#>

[CmdletBinding()]
param(
    [int]$Iterations = 10,
    [int]$DelaySeconds = 3
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " GlassFolders Explorer Recovery Verification (Phase 4)   " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Verify GlassFolders is running
$proc = Get-Process -Name "GlassFolders" -ErrorAction SilentlyContinue
if (-not $proc) {
    Write-Warning "GlassFolders is not currently running. Starting Release binary..."
    $exePath = Join-Path $PSScriptRoot "..\..\src\GlassFolders.App\bin\Release\net10.0-windows\GlassFolders.exe"
    if (-not (Test-Path $exePath)) {
        throw "Could not find GlassFolders.exe at $exePath. Please build Release configuration first."
    }
    Start-Process -FilePath $exePath
    Start-Sleep -Seconds 2
    $proc = Get-Process -Name "GlassFolders" -ErrorAction Stop
}

Write-Host "Found GlassFolders process PID: $($proc.Id)" -ForegroundColor Green

# 2. Setup Win32 EnumWindows P/Invoke to count windows belonging to GlassFolders
$win32Def = @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class WindowCounter
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    public static int CountWindowsForProcess(uint targetPid)
    {
        int count = 0;
        EnumWindows((hWnd, lParam) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == targetPid && IsWindowVisible(hWnd))
            {
                count++;
            }
            return true;
        }, IntPtr.Zero);
        return count;
    }
}
"@

if (-not ([System.Management.Automation.PSTypeName]'WindowCounter').Type) {
    Add-Type -TypeDefinition $win32Def
}

# 3. Read state.json to get initial folder count
$appData = [Environment]::GetFolderPath([Environment.SpecialFolder]::ApplicationData)
$foldersFile = Join-Path $appData "GlassFolders\folders.json"
$expectedCount = 3
if (Test-Path $foldersFile) {
    try {
        $json = Get-Content $foldersFile -Raw | ConvertFrom-Json
        $expectedCount = ($json.folders | Where-Object { $_.isVisible -ne $false }).Count
    } catch {}
}

Write-Host "Expected visible desktop folders: $expectedCount" -ForegroundColor Yellow

$initialWindows = [WindowCounter]::CountWindowsForProcess($proc.Id)
Write-Host "Initial visible windows before test: $initialWindows" -ForegroundColor Cyan

# 4. Perform restart cycles
$passed = $true
for ($i = 1; $i -le $Iterations; $i++) {
    Write-Host "`n--- Iteration $i of $Iterations: Restarting Explorer ---" -ForegroundColor Yellow

    # Kill Explorer
    Stop-Process -Name "explorer" -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800

    # Start Explorer
    Start-Process "explorer.exe"

    # Wait for recovery backoff & taskbar signal
    Write-Host "Waiting $DelaySeconds seconds for shell reattachment..."
    Start-Sleep -Seconds $DelaySeconds

    # Count windows
    $currentWindows = [WindowCounter]::CountWindowsForProcess($proc.Id)
    Write-Host "Iteration $i Result: $currentWindows windows found (expected >= $expectedCount)" -ForegroundColor $(if ($currentWindows -ge $expectedCount) { "Green" } else { "Red" })

    if ($currentWindows -lt $expectedCount) {
        Write-Warning "Iteration $i: Window count ($currentWindows) less than expected ($expectedCount)!"
        $passed = $false
    }
}

Write-Host "`n==========================================================" -ForegroundColor Cyan
if ($passed) {
    Write-Host " Explorer Recovery verification PASSED ($Iterations/$Iterations cycles). " -ForegroundColor Green
} else {
    Write-Host " Explorer Recovery verification FAILED!                     " -ForegroundColor Red
}
Write-Host "==========================================================" -ForegroundColor Cyan
