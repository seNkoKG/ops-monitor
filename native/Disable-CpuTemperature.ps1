[CmdletBinding()]
param(
    [switch]$Elevated
)

$ErrorActionPreference = 'Stop'
$taskNames = @(
    'OPS Monitor CPU Sensor',
    'PerformancePillCpuTemperature'
)

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator
    )
}

function Get-NormalizedPath {
    param([Parameter(Mandatory)][string]$Path)

    return [IO.Path]::GetFullPath(
        [Environment]::ExpandEnvironmentVariables($Path)
    ).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
}

function Test-IsChildPath {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Parent
    )

    $normalizedPath = Get-NormalizedPath $Path
    $normalizedParent = Get-NormalizedPath $Parent
    return $normalizedPath.StartsWith(
        $normalizedParent + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase
    )
}

if (-not (Test-IsAdministrator)) {
    $legacyDataDirectory = Join-Path $env:LOCALAPPDATA 'PerformancePill'
    foreach ($legacyFileName in @(
            'cpu-temperature.txt',
            'cpu-temperature-diagnostic.txt'
        )) {
        $legacyPath = Join-Path $legacyDataDirectory $legacyFileName
        try {
            if (Test-Path -LiteralPath $legacyPath) {
                Remove-Item -LiteralPath $legacyPath -Force
            }
        }
        catch {
            Write-Warning "Could not remove legacy sensor data: $legacyPath"
        }
    }

    $powerShell = (Get-Process -Id $PID).Path
    $arguments = '-NoLogo -NoProfile -ExecutionPolicy Bypass -File "{0}" -Elevated' -f (
        $MyInvocation.MyCommand.Path.Replace('"', '""')
    )
    $process = Start-Process `
        -FilePath $powerShell `
        -Verb RunAs `
        -ArgumentList $arguments `
        -Wait `
        -PassThru
    exit $process.ExitCode
}

foreach ($taskName in $taskNames) {
    $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if ($null -eq $task) {
        continue
    }

    if ($task.State -eq 'Running') {
        Stop-ScheduledTask -TaskName $taskName
    }
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
}

$programFiles = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::ProgramFiles
)
$destination = Join-Path $programFiles 'OPS Monitor Sensor'
if (-not (Test-IsChildPath -Path $destination -Parent $programFiles)) {
    throw "Refusing to remove sensor files outside $programFiles."
}

foreach ($process in @(Get-Process -Name 'OpsMonitor.SensorBridge' -ErrorAction SilentlyContinue)) {
    try {
        $path = $process.MainModule.FileName
        if ($path -and (Test-IsChildPath -Path $path -Parent $destination)) {
            Stop-Process -Id $process.Id -Force
            $process.WaitForExit(3000)
        }
    }
    finally {
        $process.Dispose()
    }
}

if (Test-Path -LiteralPath $destination) {
    Remove-Item -LiteralPath $destination -Recurse -Force
}

Write-Host "OPS Monitor's CPU temperature sensor task and broker were removed." -ForegroundColor Green
Write-Host 'PawnIO was preserved because other hardware-monitoring apps may use it.' -ForegroundColor Cyan
