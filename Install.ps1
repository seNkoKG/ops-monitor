[CmdletBinding()]
param(
    [switch]$EnableStartup,
    [switch]$Launch
)

$ErrorActionPreference = 'Stop'
$appRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$launcher = Join-Path $appRoot 'Launch-PerformancePill.vbs'
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$shortcutPath = Join-Path $startMenu 'Performance Pill.lnk'

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = (Join-Path $env:WINDIR 'System32\wscript.exe')
$shortcut.Arguments = '//B //NoLogo "{0}"' -f $launcher
$shortcut.WorkingDirectory = $appRoot
$shortcut.Description = 'Lightweight desktop performance monitor'
$shortcut.Save()

if ($EnableStartup) {
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    $command = '"{0}" //B //NoLogo "{1}"' -f (Join-Path $env:WINDIR 'System32\wscript.exe'), $launcher
    Set-ItemProperty -Path $runKey -Name 'PerformancePill' -Value $command -Type String
}

Write-Host "Installed Start menu shortcut: $shortcutPath"
if ($EnableStartup) {
    Write-Host 'Automatic startup is enabled.'
}
if ($Launch) {
    Start-Process -FilePath (Join-Path $env:WINDIR 'System32\wscript.exe') -ArgumentList @('//B', '//NoLogo', "`"$launcher`"") -WindowStyle Hidden
}
