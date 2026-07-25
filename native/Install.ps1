[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$SelfContained,

    [switch]$EnableStartup,

    [switch]$DesktopShortcut,

    [switch]$Launch,

    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$packageKind = if ($SelfContained) {
    'win-x64-self-contained'
}
else {
    'framework-dependent'
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

function Get-InstalledProcesses {
    param([Parameter(Mandatory)][string]$InstallDirectory)

    $result = @()
    foreach ($process in @(Get-Process -Name 'OpsMonitor.Widget', 'OpsMonitor.Studio' -ErrorAction SilentlyContinue)) {
        try {
            $executablePath = $process.MainModule.FileName
            if ($executablePath -and (Test-IsChildPath -Path $executablePath -Parent $InstallDirectory)) {
                $result += $process
                continue
            }
        }
        catch [System.ComponentModel.Win32Exception] {
            # An inaccessible process cannot be an install-file lock we can safely resolve.
        }

        $process.Dispose()
    }

    return $result
}

function New-Shortcut {
    param(
        [Parameter(Mandatory)]$Shell,
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$TargetPath,
        [string]$Arguments = '',
        [string]$WorkingDirectory = '',
        [string]$Description = ''
    )

    $shortcut = $Shell.CreateShortcut($Path)
    try {
        $shortcut.TargetPath = $TargetPath
        $shortcut.Arguments = $Arguments
        $shortcut.WorkingDirectory = $WorkingDirectory
        $shortcut.Description = $Description
        $shortcut.Save()
    }
    finally {
        if ([Runtime.InteropServices.Marshal]::IsComObject($shortcut)) {
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
        }
    }
}

$source = if (Test-Path -LiteralPath (Join-Path $root 'OpsMonitor.Widget.exe')) {
    $root
}
else {
    Join-Path $root "artifacts\publish\$packageKind"
}

if (-not (Test-Path -LiteralPath (Join-Path $source 'OpsMonitor.Widget.exe')) -or
    -not (Test-Path -LiteralPath (Join-Path $source 'OpsMonitor.Studio.exe'))) {
    if ($NoBuild -or (Test-Path -LiteralPath (Join-Path $root 'OpsMonitor.Widget.exe'))) {
        throw "A complete $packageKind package was not found at $source."
    }

    $buildScript = Join-Path $root 'Build.ps1'
    if (-not (Test-Path -LiteralPath $buildScript)) {
        throw "A complete package was not found and Build.ps1 is unavailable."
    }

    & $buildScript -Configuration Release -Publish -SelfContained:$SelfContained
    if ($LASTEXITCODE -ne 0) {
        throw 'The release package could not be built.'
    }
}

$localPrograms = Join-Path $env:LOCALAPPDATA 'Programs'
$installDirectory = Join-Path $localPrograms 'OPS Monitor'
$resolvedPrograms = Get-NormalizedPath $localPrograms
$resolvedInstall = Get-NormalizedPath $installDirectory
if (-not (Test-IsChildPath -Path $resolvedInstall -Parent $resolvedPrograms)) {
    throw "Refusing to install outside $resolvedPrograms."
}

$blockingProcesses = @(Get-InstalledProcesses -InstallDirectory $resolvedInstall)
if ($blockingProcesses.Count -gt 0) {
    $identifiers = ($blockingProcesses | ForEach-Object { "$($_.ProcessName) ($($_.Id))" }) -join ', '
    $blockingProcesses | ForEach-Object { $_.Dispose() }
    throw "Close the installed OPS Monitor apps before updating: $identifiers."
}

$stage = Join-Path $localPrograms ('.OPS-Monitor-installing-' + [Guid]::NewGuid().ToString('N'))
$backup = Join-Path $localPrograms ('.OPS-Monitor-previous-' + [DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff'))
$installed = $false

if ($PSCmdlet.ShouldProcess($resolvedInstall, "Install OPS Monitor from $source")) {
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    try {
        Copy-Item -Path (Join-Path $source '*') -Destination $stage -Recurse -Force
        foreach ($requiredExecutable in @('OpsMonitor.Widget.exe', 'OpsMonitor.Studio.exe')) {
            if (-not (Test-Path -LiteralPath (Join-Path $stage $requiredExecutable))) {
                throw "The staged installation is incomplete: $requiredExecutable is missing."
            }
        }

        if (Test-Path -LiteralPath $resolvedInstall) {
            Move-Item -LiteralPath $resolvedInstall -Destination $backup
        }

        try {
            Move-Item -LiteralPath $stage -Destination $resolvedInstall
            $installed = $true
        }
        catch {
            if (Test-Path -LiteralPath $backup) {
                Move-Item -LiteralPath $backup -Destination $resolvedInstall
            }
            throw
        }

        if (Test-Path -LiteralPath $backup) {
            Remove-Item -LiteralPath $backup -Recurse -Force
        }
    }
    finally {
        if (Test-Path -LiteralPath $stage) {
            Remove-Item -LiteralPath $stage -Recurse -Force
        }
    }
}

if ($installed) {
    $startMenuFolder = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\OPS Monitor'
    New-Item -ItemType Directory -Path $startMenuFolder -Force | Out-Null
    $shell = New-Object -ComObject WScript.Shell
    try {
        New-Shortcut `
            -Shell $shell `
            -Path (Join-Path $startMenuFolder 'OPS Monitor Widget.lnk') `
            -TargetPath (Join-Path $resolvedInstall 'OpsMonitor.Widget.exe') `
            -WorkingDirectory $resolvedInstall `
            -Description 'Open the OPS Monitor desktop widget'
        New-Shortcut `
            -Shell $shell `
            -Path (Join-Path $startMenuFolder 'OPS Monitor Studio.lnk') `
            -TargetPath (Join-Path $resolvedInstall 'OpsMonitor.Studio.exe') `
            -WorkingDirectory $resolvedInstall `
            -Description 'Customize OPS Monitor'

        $uninstallScript = Join-Path $resolvedInstall 'Uninstall.ps1'
        if (Test-Path -LiteralPath $uninstallScript) {
            $powerShell = (Get-Process -Id $PID).Path
            New-Shortcut `
                -Shell $shell `
                -Path (Join-Path $startMenuFolder 'Uninstall OPS Monitor.lnk') `
                -TargetPath $powerShell `
                -Arguments ('-NoProfile -ExecutionPolicy Bypass -File "{0}" -StopRunningApps' -f $uninstallScript) `
                -WorkingDirectory $env:TEMP `
                -Description 'Uninstall OPS Monitor'
        }

        if ($DesktopShortcut) {
            $desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
            New-Shortcut `
                -Shell $shell `
                -Path (Join-Path $desktop 'OPS Monitor.lnk') `
                -TargetPath (Join-Path $resolvedInstall 'OpsMonitor.Widget.exe') `
                -WorkingDirectory $resolvedInstall `
                -Description 'Open the OPS Monitor desktop widget'
        }
    }
    finally {
        if ([Runtime.InteropServices.Marshal]::IsComObject($shell)) {
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
        }
    }

    if ($EnableStartup) {
        $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
        New-Item -Path $runKey -Force | Out-Null
        $startupCommand = '"{0}"' -f (Join-Path $resolvedInstall 'OpsMonitor.Widget.exe')
        Set-ItemProperty -Path $runKey -Name 'OPS Monitor Widget' -Value $startupCommand -Type String
    }

    Write-Host "OPS Monitor installed to $resolvedInstall" -ForegroundColor Green
    Write-Host "Start menu shortcuts: $startMenuFolder" -ForegroundColor Cyan
    if ($EnableStartup) {
        Write-Host 'Automatic startup is enabled for the current Windows user.' -ForegroundColor Cyan
    }

    if ($Launch) {
        Start-Process `
            -FilePath (Join-Path $resolvedInstall 'OpsMonitor.Widget.exe') `
            -WorkingDirectory $resolvedInstall
    }
}
