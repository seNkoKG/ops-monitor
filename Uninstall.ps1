[CmdletBinding()]
param(
    [switch]$RemoveSettings
)

$shortcutPath = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Performance Pill.lnk'
if (Test-Path -LiteralPath $shortcutPath) {
    Remove-Item -LiteralPath $shortcutPath -Force
}

Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'PerformancePill' -ErrorAction SilentlyContinue

try {
    Stop-ScheduledTask -TaskName 'PerformancePillCpuTemperature' -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName 'PerformancePillCpuTemperature' -Confirm:$false -ErrorAction Stop
}
catch {
    Write-Warning 'The elevated CPU sensor task could not be removed. Run Uninstall.ps1 as administrator to remove it.'
}

if ($RemoveSettings) {
    $settingsFolder = Join-Path $env:LOCALAPPDATA 'PerformancePill'
    $resolvedSettings = [IO.Path]::GetFullPath($settingsFolder)
    $resolvedLocalData = [IO.Path]::GetFullPath($env:LOCALAPPDATA)
    if ($resolvedSettings.StartsWith($resolvedLocalData, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedSettings)) {
        Remove-Item -LiteralPath $resolvedSettings -Recurse -Force
    }
}

Write-Host 'Performance Pill startup entries, sensor task, and Start menu shortcut removed.'
