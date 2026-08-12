[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$SelfContained,

    [switch]$EnableStartup,

    [switch]$DesktopShortcut,

    [switch]$EnableCpuTemperature,

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

function Assert-WindowsDesktopRuntime {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    $dotnet = if ($dotnetCommand) {
        $dotnetCommand.Source
    }
    else {
        Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    }

    if (-not (Test-Path -LiteralPath $dotnet)) {
        throw '.NET 10 Desktop Runtime is required for the framework-dependent package. Use the self-contained package or install Microsoft.WindowsDesktop.App 10.'
    }

    $runtimeOutput = @(& $dotnet --list-runtimes 2>&1)
    if ($LASTEXITCODE -ne 0 -or
        -not ($runtimeOutput | Where-Object { $_ -match '^Microsoft\.WindowsDesktop\.App\s+10\.' })) {
        throw '.NET 10 Desktop Runtime is required for the framework-dependent package. Use the self-contained package or install Microsoft.WindowsDesktop.App 10.'
    }
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

if (-not $SelfContained) {
    Assert-WindowsDesktopRuntime
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
$backupCreated = $false

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
            $backupCreated = $true
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
    }
    finally {
        if (Test-Path -LiteralPath $stage) {
            Remove-Item -LiteralPath $stage -Recurse -Force
        }
    }
}

if ($installed) {
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    $startupValueName = 'OPS Monitor Widget'
    $startupEntryWasPresent = $false
    $startupCommandBefore = $null
    if (Test-Path -LiteralPath $runKey) {
        $runValuesBefore = Get-ItemProperty -Path $runKey -ErrorAction SilentlyContinue
        if ($null -ne $runValuesBefore) {
            $startupProperty = $runValuesBefore.PSObject.Properties[$startupValueName]
            if ($null -ne $startupProperty) {
                $startupEntryWasPresent = $true
                $startupCommandBefore = $startupProperty.Value
            }
        }
    }

    $launchedProcess = $null
    try {
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

            $powerShell = (Get-Process -Id $PID).Path
            $uninstallScript = Join-Path $resolvedInstall 'Uninstall.ps1'
            if (Test-Path -LiteralPath $uninstallScript) {
                New-Shortcut `
                    -Shell $shell `
                    -Path (Join-Path $startMenuFolder 'Uninstall OPS Monitor.lnk') `
                    -TargetPath $powerShell `
                    -Arguments ('-NoProfile -ExecutionPolicy Bypass -File "{0}" -StopRunningApps -RemoveCpuSensor' -f $uninstallScript) `
                    -WorkingDirectory $env:TEMP `
                    -Description 'Uninstall OPS Monitor'
            }

            $enableCpuSensorScript = Join-Path $resolvedInstall 'Enable-CpuTemperature.ps1'
            if (Test-Path -LiteralPath $enableCpuSensorScript) {
                New-Shortcut `
                    -Shell $shell `
                    -Path (Join-Path $startMenuFolder 'Enable CPU Temperature.lnk') `
                    -TargetPath $powerShell `
                    -Arguments ('-NoLogo -NoProfile -ExecutionPolicy Bypass -File "{0}"' -f $enableCpuSensorScript) `
                    -WorkingDirectory $resolvedInstall `
                    -Description 'Enable the secure OPS Monitor CPU temperature sensor'
            }

            $disableCpuSensorScript = Join-Path $resolvedInstall 'Disable-CpuTemperature.ps1'
            if (Test-Path -LiteralPath $disableCpuSensorScript) {
                New-Shortcut `
                    -Shell $shell `
                    -Path (Join-Path $startMenuFolder 'Disable CPU Temperature.lnk') `
                    -TargetPath $powerShell `
                    -Arguments ('-NoLogo -NoProfile -ExecutionPolicy Bypass -File "{0}"' -f $disableCpuSensorScript) `
                    -WorkingDirectory $resolvedInstall `
                    -Description 'Remove the OPS Monitor CPU sensor task and broker'
            }

            $updateScript = Join-Path $resolvedInstall 'Update.ps1'
            if (Test-Path -LiteralPath $updateScript) {
                New-Shortcut `
                    -Shell $shell `
                    -Path (Join-Path $startMenuFolder 'Check for OPS Monitor updates.lnk') `
                    -TargetPath $powerShell `
                    -Arguments ('-NoLogo -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "{0}" -Interactive' -f $updateScript) `
                    -WorkingDirectory $resolvedInstall `
                    -Description 'Check for and install a verified OPS Monitor update'
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
            New-Item -Path $runKey -Force | Out-Null
            $startupCommand = '"{0}"' -f (Join-Path $resolvedInstall 'OpsMonitor.Widget.exe')
            Set-ItemProperty -Path $runKey -Name $startupValueName -Value $startupCommand -Type String
        }

        if ($Launch) {
            $launchedProcess = Start-Process `
                -FilePath (Join-Path $resolvedInstall 'OpsMonitor.Widget.exe') `
                -WorkingDirectory $resolvedInstall `
                -PassThru
            if ($null -eq $launchedProcess) {
                throw 'Windows did not return a process for the installed Widget.'
            }

            if ($launchedProcess.WaitForExit(4000)) {
                throw "The installed Widget exited during its startup check with code $($launchedProcess.ExitCode)."
            }
        }

        if ($EnableCpuTemperature) {
            $enableCpuSensorScript = Join-Path $resolvedInstall 'Enable-CpuTemperature.ps1'
            if (-not (Test-Path -LiteralPath $enableCpuSensorScript)) {
                throw 'The installed CPU sensor setup script is missing.'
            }

            $sensorSetup = Start-Process `
                -FilePath $powerShell `
                -ArgumentList (
                    '-NoLogo -NoProfile -ExecutionPolicy Bypass -File "{0}"' -f
                    $enableCpuSensorScript
                ) `
                -Wait `
                -PassThru
            if ($sensorSetup.ExitCode -ne 0) {
                throw "CPU temperature sensor setup exited with code $($sensorSetup.ExitCode)."
            }
            $sensorSetup.Dispose()
        }

        if ($backupCreated -and (Test-Path -LiteralPath $backup)) {
            Remove-Item -LiteralPath $backup -Recurse -Force
            $backupCreated = $false
        }

        if ($null -ne $launchedProcess) {
            $launchedProcess.Dispose()
        }
        $launchedProcess = $null

        Write-Host "OPS Monitor installed to $resolvedInstall" -ForegroundColor Green
        Write-Host "Start menu shortcuts: $startMenuFolder" -ForegroundColor Cyan
        if ($EnableStartup) {
            Write-Host 'Automatic startup is enabled for the current Windows user.' -ForegroundColor Cyan
        }
    }
    catch {
        $failure = $_
        if ($null -ne $launchedProcess) {
            try {
                if (-not $launchedProcess.HasExited) {
                    [void]$launchedProcess.CloseMainWindow()
                    if (-not $launchedProcess.WaitForExit(1500)) {
                        Stop-Process -Id $launchedProcess.Id -Force
                        $launchedProcess.WaitForExit(1500)
                    }
                }
            }
            finally {
                $launchedProcess.Dispose()
            }
        }

        if (Test-Path -LiteralPath $resolvedInstall) {
            Remove-Item -LiteralPath $resolvedInstall -Recurse -Force
        }
        if ($backupCreated -and (Test-Path -LiteralPath $backup)) {
            Move-Item -LiteralPath $backup -Destination $resolvedInstall
            $backupCreated = $false
        }

        if ($EnableStartup) {
            if ($startupEntryWasPresent) {
                New-Item -Path $runKey -Force | Out-Null
                Set-ItemProperty `
                    -Path $runKey `
                    -Name $startupValueName `
                    -Value $startupCommandBefore `
                    -Type String
            }
            else {
                Remove-ItemProperty `
                    -Path $runKey `
                    -Name $startupValueName `
                    -ErrorAction SilentlyContinue
            }
        }

        throw "OPS Monitor installation failed and the previous program state was restored. $($failure.Exception.Message)"
    }
}
