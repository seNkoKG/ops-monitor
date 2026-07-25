[CmdletBinding()]
param(
    [switch]$Elevated
)

$ErrorActionPreference = 'Stop'
$appRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourcePath = Join-Path $appRoot 'src\CpuTemperatureBridge.cs'
$bridgePath = Join-Path $appRoot 'CpuTemperatureBridge.exe'
$taskName = 'PerformancePillCpuTemperature'
$cliPath = Join-Path $env:ProgramFiles 'AMD\RyzenMasterSDK\AMDRyzenMasterCLI\bin-prebuilt\AMDRyzenMasterCLI.exe'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdministrator) {
    $arguments = @(
        '-NoLogo',
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', "`"$($MyInvocation.MyCommand.Path)`"",
        '-Elevated'
    )
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $arguments -WindowStyle Hidden
    Write-Host 'Approve the Windows prompt to enable the CPU temperature sensor.'
    exit 0
}

if (-not (Test-Path -LiteralPath $cliPath)) {
    throw 'AMD Ryzen Master SDK was not found on this computer.'
}

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw '.NET Framework C# compiler was not found.'
}

& $compiler /nologo /optimize+ /target:winexe "/out:$bridgePath" $sourcePath
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $bridgePath)) {
    throw 'The CPU temperature bridge could not be built.'
}

$userId = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$action = New-ScheduledTaskAction -Execute $bridgePath -WorkingDirectory $appRoot
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $userId
$taskPrincipal = New-ScheduledTaskPrincipal -UserId $userId -LogonType Interactive -RunLevel Highest
$taskSettings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -MultipleInstances IgnoreNew

Register-ScheduledTask `
    -TaskName $taskName `
    -Action $action `
    -Trigger $trigger `
    -Principal $taskPrincipal `
    -Settings $taskSettings `
    -Description 'Reads AMD CPU temperature for the Performance Pill widget.' `
    -Force | Out-Null

Start-ScheduledTask -TaskName $taskName

$settingsFolder = Join-Path $env:LOCALAPPDATA 'PerformancePill'
$settingsPath = Join-Path $settingsFolder 'settings.json'
if (-not (Test-Path -LiteralPath $settingsFolder)) {
    New-Item -ItemType Directory -Path $settingsFolder -Force | Out-Null
}
$settings = if (Test-Path -LiteralPath $settingsPath) {
    Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
} else {
    [pscustomobject]@{}
}
if ($null -eq $settings.PSObject.Properties['CpuSensorEnabled']) {
    $settings | Add-Member -NotePropertyName CpuSensorEnabled -NotePropertyValue $true
} else {
    $settings.CpuSensorEnabled = $true
}
[IO.File]::WriteAllText(
    $settingsPath,
    ($settings | ConvertTo-Json),
    [Text.UTF8Encoding]::new($false))

Write-Host 'CPU temperature access is enabled and the sensor bridge is running.'
