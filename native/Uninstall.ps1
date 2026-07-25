[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$RemoveUserData,

    [switch]$StopRunningApps
)

$ErrorActionPreference = 'Stop'

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

$localPrograms = Join-Path $env:LOCALAPPDATA 'Programs'
$installDirectory = Join-Path $localPrograms 'OPS Monitor'
$resolvedPrograms = Get-NormalizedPath $localPrograms
$resolvedInstall = Get-NormalizedPath $installDirectory
if (-not (Test-IsChildPath -Path $resolvedInstall -Parent $resolvedPrograms)) {
    throw "Refusing to uninstall outside $resolvedPrograms."
}

$installedProcesses = @()
foreach ($process in @(Get-Process -Name 'OpsMonitor.Widget', 'OpsMonitor.Studio' -ErrorAction SilentlyContinue)) {
    try {
        $executablePath = $process.MainModule.FileName
        if ($executablePath -and (Test-IsChildPath -Path $executablePath -Parent $resolvedInstall)) {
            $installedProcesses += $process
            continue
        }
    }
    catch [System.ComponentModel.Win32Exception] {
        # An inaccessible process is not stopped by this per-user uninstaller.
    }

    $process.Dispose()
}

if ($installedProcesses.Count -gt 0 -and -not $StopRunningApps) {
    $identifiers = ($installedProcesses | ForEach-Object { "$($_.ProcessName) ($($_.Id))" }) -join ', '
    $installedProcesses | ForEach-Object { $_.Dispose() }
    throw "OPS Monitor is running ($identifiers). Close it or rerun with -StopRunningApps."
}

foreach ($process in $installedProcesses) {
    try {
        if ($process.MainWindowHandle -ne 0) {
            [void]$process.CloseMainWindow()
        }
        if (-not $process.WaitForExit(2000)) {
            Stop-Process -Id $process.Id -Force
            $process.WaitForExit(2000)
        }
    }
    finally {
        $process.Dispose()
    }
}

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runValues = Get-ItemProperty -Path $runKey -ErrorAction SilentlyContinue
$startupCommand = if ($null -ne $runValues) {
    $runValues.'OPS Monitor Widget'
}
$installedWidgetCommand = '"{0}"' -f (Join-Path $resolvedInstall 'OpsMonitor.Widget.exe')
$ownsStartupEntry = $startupCommand -is [string] -and (
    $startupCommand.Equals($installedWidgetCommand, [StringComparison]::OrdinalIgnoreCase) -or
    $startupCommand.StartsWith(
        $installedWidgetCommand + ' ',
        [StringComparison]::OrdinalIgnoreCase
    )
)
if ($ownsStartupEntry -and
    $PSCmdlet.ShouldProcess('HKCU Windows startup', 'Remove OPS Monitor startup entry')) {
    Remove-ItemProperty -Path $runKey -Name 'OPS Monitor Widget'
}
elseif ($startupCommand -is [string] -and -not $ownsStartupEntry) {
    Write-Host 'Preserved the OPS Monitor startup entry because it points to another copy.' -ForegroundColor Yellow
}

$startMenuFolder = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\OPS Monitor'
$desktopShortcut = Join-Path (
    [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
) 'OPS Monitor.lnk'

if ($PSCmdlet.ShouldProcess($startMenuFolder, 'Remove OPS Monitor Start menu shortcuts') -and
    (Test-Path -LiteralPath $startMenuFolder)) {
    Remove-Item -LiteralPath $startMenuFolder -Recurse -Force
}
if (Test-Path -LiteralPath $desktopShortcut) {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($desktopShortcut)
    try {
        $shortcutTarget = $shortcut.TargetPath
        $ownsDesktopShortcut = $shortcutTarget -and
            (Test-IsChildPath -Path $shortcutTarget -Parent $resolvedInstall)
    }
    finally {
        if ([Runtime.InteropServices.Marshal]::IsComObject($shortcut)) {
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
        }
        if ([Runtime.InteropServices.Marshal]::IsComObject($shell)) {
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
        }
    }

    if ($ownsDesktopShortcut -and
        $PSCmdlet.ShouldProcess($desktopShortcut, 'Remove OPS Monitor desktop shortcut')) {
        Remove-Item -LiteralPath $desktopShortcut -Force
    }
}

if ($PSCmdlet.ShouldProcess($resolvedInstall, 'Remove OPS Monitor program files') -and
    (Test-Path -LiteralPath $resolvedInstall)) {
    Set-Location -LiteralPath $env:TEMP
    [Environment]::CurrentDirectory = $env:TEMP
    Remove-Item -LiteralPath $resolvedInstall -Recurse -Force
}

if ($RemoveUserData) {
    $userData = Get-NormalizedPath (Join-Path $env:LOCALAPPDATA 'OPS Monitor')
    $resolvedLocalData = Get-NormalizedPath $env:LOCALAPPDATA
    if (-not (Test-IsChildPath -Path $userData -Parent $resolvedLocalData)) {
        throw "Refusing to remove user data outside $resolvedLocalData."
    }

    if ($PSCmdlet.ShouldProcess($userData, 'Remove OPS Monitor settings and history') -and
        (Test-Path -LiteralPath $userData)) {
        Remove-Item -LiteralPath $userData -Recurse -Force
    }
}

Write-Host 'OPS Monitor was uninstalled. Saved settings were preserved unless -RemoveUserData was used.' -ForegroundColor Green
