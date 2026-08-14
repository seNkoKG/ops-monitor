[CmdletBinding()]
param(
    [string]$InstallDirectory = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'
$resolvedInstall = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar
)
$localPrograms = [IO.Path]::GetFullPath(
    (Join-Path $env:LOCALAPPDATA 'Programs')
).TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar
)
if (-not $resolvedInstall.StartsWith(
        $localPrograms + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to manage applications outside $localPrograms."
}
foreach ($process in @(Get-Process -Name 'OpsMonitor.Widget', 'OpsMonitor.Studio' -ErrorAction SilentlyContinue)) {
    try {
        $executablePath = $process.MainModule.FileName
        $isInstalledCopy = $executablePath -and
            [IO.Path]::GetFullPath($executablePath).StartsWith(
                $resolvedInstall + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase
            )
        if (-not $isInstalledCopy) {
            continue
        }

        if ($process.MainWindowHandle -ne 0) {
            [void]$process.CloseMainWindow()
        }
        if (-not $process.WaitForExit(3000)) {
            Stop-Process -Id $process.Id -Force
            if (-not $process.WaitForExit(3000)) {
                throw "OPS Monitor process $($process.Id) did not exit."
            }
        }
    }
    catch [System.ComponentModel.Win32Exception] {
        # An inaccessible process is not part of this current-user install.
    }
    finally {
        $process.Dispose()
    }
}
