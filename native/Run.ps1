[CmdletBinding()]
param(
    [ValidateSet('Widget', 'Studio')]
    [string]$Application = 'Widget',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [string[]]$ArgumentList = @(),

    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$projectName = "OpsMonitor.$Application"
$executable = Join-Path $root "src\$projectName\bin\$Configuration\net10.0-windows\$projectName.exe"

if (-not $NoBuild -or -not (Test-Path -LiteralPath $executable)) {
    & (Join-Path $root 'Build.ps1') -Configuration $Configuration
}

if (-not (Test-Path -LiteralPath $executable)) {
    throw "Executable was not produced: $executable"
}

$startParameters = @{
    FilePath = $executable
    WorkingDirectory = Split-Path -Parent $executable
    PassThru = $true
}
if ($ArgumentList.Count -gt 0) {
    $startParameters.ArgumentList = $ArgumentList
}

$process = Start-Process @startParameters

Write-Output $process
