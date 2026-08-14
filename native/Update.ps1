[CmdletBinding(SupportsShouldProcess)]
param(
    [uri]$ManifestUri = 'https://senkokg.github.io/ops-monitor/release-manifest.json',

    [switch]$CheckOnly,

    [switch]$Force,

    [switch]$NoLaunch,

    [switch]$Interactive
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = $PSScriptRoot
$tempRoot = [IO.Path]::GetFullPath($env:TEMP).TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar
)
$stage = Join-Path $tempRoot ('.ops-monitor-update-' + [Guid]::NewGuid().ToString('N'))

function Show-UpdateMessage {
    param(
        [Parameter(Mandatory)][string]$Text,
        [string]$Caption = 'OPS Monitor update',
        [ValidateSet('Information', 'Error')][string]$Icon = 'Information'
    )

    if (-not $Interactive) {
        Write-Host $Text
        return
    }

    Add-Type -AssemblyName System.Windows.Forms
    $messageIcon = if ($Icon -eq 'Error') {
        [Windows.Forms.MessageBoxIcon]::Error
    }
    else {
        [Windows.Forms.MessageBoxIcon]::Information
    }
    [void][Windows.Forms.MessageBox]::Show(
        $Text,
        $Caption,
        [Windows.Forms.MessageBoxButtons]::OK,
        $messageIcon
    )
}

function Get-InstalledVersion {
    $versionPath = Join-Path $root 'VERSION'
    if (Test-Path -LiteralPath $versionPath) {
        return [version](Get-Content -LiteralPath $versionPath -Raw).Trim()
    }

    $widget = Join-Path $root 'OpsMonitor.Widget.exe'
    if (Test-Path -LiteralPath $widget) {
        return [version]([Diagnostics.FileVersionInfo]::GetVersionInfo($widget).FileVersion)
    }

    throw 'The installed OPS Monitor version could not be determined.'
}

function Stop-InstalledApps {
    foreach ($process in @(Get-Process -Name 'OpsMonitor.Widget', 'OpsMonitor.Studio' -ErrorAction SilentlyContinue)) {
        try {
            $path = $process.MainModule.FileName
            if ($path -and [IO.Path]::GetFullPath($path).StartsWith(
                    [IO.Path]::GetFullPath($root) + [IO.Path]::DirectorySeparatorChar,
                    [StringComparison]::OrdinalIgnoreCase)) {
                Stop-Process -Id $process.Id -Force
                $process.WaitForExit(5000)
            }
        }
        catch [System.ComponentModel.Win32Exception] {
            # An inaccessible process cannot lock this per-user installation.
        }
        finally {
            $process.Dispose()
        }
    }
}

try {
    $currentVersion = Get-InstalledVersion
    $manifest = Invoke-RestMethod -Uri $ManifestUri -TimeoutSec 20
    if ($manifest.schemaVersion -ne 1 -or [string]::IsNullOrWhiteSpace($manifest.version)) {
        throw 'The release manifest format is unsupported.'
    }

    $availableVersion = [version]$manifest.version
    $packageKindPath = Join-Path $root '.ops-package-kind'
    $packageKind = if (Test-Path -LiteralPath $packageKindPath) {
        (Get-Content -LiteralPath $packageKindPath -Raw).Trim()
    }
    else {
        'framework-dependent'
    }
    $installerProperty = $manifest.packages.PSObject.Properties['windows-installer']
    $usesWindowsInstaller = $null -ne $installerProperty
    $packageProperty = if ($usesWindowsInstaller) {
        $installerProperty
    }
    else {
        $manifest.packages.PSObject.Properties[$packageKind]
    }
    if ($null -eq $packageProperty) {
        throw "The release does not provide a $packageKind package."
    }
    $package = $packageProperty.Value
    $updateAvailable = $availableVersion -gt $currentVersion

    $status = [pscustomobject]@{
        CurrentVersion = $currentVersion.ToString(3)
        AvailableVersion = $availableVersion.ToString(3)
        PackageKind = $packageKind
        DeliveryKind = if ($usesWindowsInstaller) { 'windows-installer' } else { 'zip' }
        UpdateAvailable = $updateAvailable
        ReleaseUrl = [string]$manifest.releaseUrl
    }
    if ($CheckOnly) {
        Write-Output $status
        return
    }

    if (-not $updateAvailable -and -not $Force) {
        Show-UpdateMessage "OPS Monitor $($currentVersion.ToString(3)) is already current."
        Write-Output $status
        return
    }

    if ($Interactive -and -not $Force) {
        Add-Type -AssemblyName System.Windows.Forms
        $answer = [Windows.Forms.MessageBox]::Show(
            "Update OPS Monitor $($currentVersion.ToString(3)) to $($availableVersion.ToString(3)) now? The widget and Studio will restart.",
            'OPS Monitor update',
            [Windows.Forms.MessageBoxButtons]::YesNo,
            [Windows.Forms.MessageBoxIcon]::Question
        )
        if ($answer -ne [Windows.Forms.DialogResult]::Yes) {
            return
        }
    }

    if (-not $PSCmdlet.ShouldProcess(
            "OPS Monitor $($currentVersion.ToString(3))",
            "Install $($availableVersion.ToString(3))")) {
        return
    }

    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    $download = Join-Path $stage $(if ($usesWindowsInstaller) { 'setup.exe' } else { 'release.zip' })
    Invoke-WebRequest -Uri ([uri]$package.url) -OutFile $download -TimeoutSec 120
    $actualHash = (Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash
    if (-not $actualHash.Equals([string]$package.sha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The downloaded release failed SHA-256 verification.'
    }

    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    $startupEnabled = $null -ne (
        Get-ItemProperty -Path $runKey -Name 'OPS Monitor Widget' -ErrorAction SilentlyContinue
    )
    $desktopShortcut = Join-Path (
        [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
    ) 'OPS Monitor.lnk'
    $desktopShortcutEnabled = Test-Path -LiteralPath $desktopShortcut
    Stop-InstalledApps

    if ($usesWindowsInstaller) {
        $installerVersionText = [Diagnostics.FileVersionInfo]::GetVersionInfo($download).ProductVersion
        $installerVersion = [version]$installerVersionText
        if ($installerVersion -ne $availableVersion) {
            throw "The installer version $installerVersion does not match manifest version $availableVersion."
        }

        $selectedTasks = @()
        if ($startupEnabled) {
            $selectedTasks += 'startup'
        }
        if ($desktopShortcutEnabled) {
            $selectedTasks += 'desktopicon'
        }
        $setupArguments = @(
            '/VERYSILENT',
            '/SUPPRESSMSGBOXES',
            '/NORESTART',
            '/NORESTARTAPPLICATIONS',
            '/CURRENTUSER',
            ('/TASKS=' + ($selectedTasks -join ','))
        )
        $setup = Start-Process `
            -FilePath $download `
            -ArgumentList $setupArguments `
            -Wait `
            -PassThru
        try {
            if ($setup.ExitCode -ne 0) {
                throw "The Windows installer exited with code $($setup.ExitCode)."
            }
        }
        finally {
            $setup.Dispose()
        }

        if (-not $NoLaunch) {
            $widget = Join-Path $root 'OpsMonitor.Widget.exe'
            if (-not (Test-Path -LiteralPath $widget)) {
                throw 'The updated Widget executable is missing.'
            }
            Start-Process -FilePath $widget -WorkingDirectory $root
        }
    }
    else {
        $expanded = Join-Path $stage 'package'
        Expand-Archive -LiteralPath $download -DestinationPath $expanded
        foreach ($required in @('OpsMonitor.Widget.exe', 'OpsMonitor.Studio.exe', 'Install.ps1', 'VERSION')) {
            if (-not (Test-Path -LiteralPath (Join-Path $expanded $required))) {
                throw "The downloaded package is incomplete: $required is missing."
            }
        }
        $packageVersion = [version](Get-Content -LiteralPath (Join-Path $expanded 'VERSION') -Raw).Trim()
        if ($packageVersion -ne $availableVersion) {
            throw "The package version $packageVersion does not match manifest version $availableVersion."
        }

        $installArguments = @{
            NoBuild = $true
            SelfContained = $packageKind -eq 'win-x64-self-contained'
            EnableStartup = $startupEnabled
            DesktopShortcut = $desktopShortcutEnabled
            Launch = -not $NoLaunch
        }
        & (Join-Path $expanded 'Install.ps1') @installArguments
        if ($LASTEXITCODE -ne 0) {
            throw "The installer exited with code $LASTEXITCODE."
        }
    }

    Show-UpdateMessage "OPS Monitor $($availableVersion.ToString(3)) was installed successfully."
}
catch {
    Show-UpdateMessage $_.Exception.Message 'OPS Monitor update failed' 'Error'
    throw
}
finally {
    $resolvedStage = [IO.Path]::GetFullPath($stage)
    if ($resolvedStage.StartsWith(
            $tempRoot + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedStage)) {
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}
