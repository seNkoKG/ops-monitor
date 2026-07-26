[CmdletBinding()]
param(
    [switch]$Elevated,

    [switch]$NoStart,

    [string]$TargetUserSid
)

$ErrorActionPreference = 'Stop'
$taskName = 'OPS Monitor CPU Sensor'
$legacyTaskName = 'PerformancePillCpuTemperature'
$sourceDirectory = Join-Path $PSScriptRoot 'SensorBridge'
$sourceExecutable = Join-Path $sourceDirectory 'OpsMonitor.SensorBridge.exe'

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

function Grant-TaskRunAccess {
    param(
        [Parameter(Mandatory)][string]$RegisteredTaskName,
        [Parameter(Mandatory)]
        [Security.Principal.SecurityIdentifier]$SecurityIdentifier
    )

    $service = New-Object -ComObject 'Schedule.Service'
    $folder = $null
    $registeredTask = $null
    try {
        $service.Connect()
        $folder = $service.GetFolder('\')
        $registeredTask = $folder.GetTask($RegisteredTaskName)
        $descriptor = [Security.AccessControl.RawSecurityDescriptor]::new(
            $registeredTask.GetSecurityDescriptor(15)
        )
        # FILE_GENERIC_READ | FILE_GENERIC_EXECUTE. This permits the widget
        # user to inspect and run the SYSTEM task, but not edit or delete it.
        $fileReadAndExecute = 0x1200A9
        $alreadyGranted = @($descriptor.DiscretionaryAcl) | Where-Object {
            $_ -is [Security.AccessControl.CommonAce] -and
            $_.AceQualifier -eq [Security.AccessControl.AceQualifier]::AccessAllowed -and
            $_.SecurityIdentifier -eq $SecurityIdentifier -and
            ($_.AccessMask -band $fileReadAndExecute) -eq $fileReadAndExecute
        }
        if ($alreadyGranted.Count -eq 0) {
            $ace = [Security.AccessControl.CommonAce]::new(
                [Security.AccessControl.AceFlags]::None,
                [Security.AccessControl.AceQualifier]::AccessAllowed,
                $fileReadAndExecute,
                $SecurityIdentifier,
                $false,
                $null
            )
            $descriptor.DiscretionaryAcl.InsertAce(
                $descriptor.DiscretionaryAcl.Count,
                $ace
            )
            $registeredTask.SetSecurityDescriptor(
                $descriptor.GetSddlForm(
                    [Security.AccessControl.AccessControlSections]::All
                ),
                0
            )
        }
    }
    finally {
        foreach ($comObject in @($registeredTask, $folder, $service)) {
            if ($null -ne $comObject -and
                [Runtime.InteropServices.Marshal]::IsComObject($comObject)) {
                [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject(
                    $comObject
                )
            }
        }
    }
}

function Get-TaskSecurityDescriptor {
    param([Parameter(Mandatory)][string]$RegisteredTaskName)

    $service = New-Object -ComObject 'Schedule.Service'
    $folder = $null
    $registeredTask = $null
    try {
        $service.Connect()
        $folder = $service.GetFolder('\')
        $registeredTask = $folder.GetTask($RegisteredTaskName)
        return $registeredTask.GetSecurityDescriptor(15)
    }
    finally {
        foreach ($comObject in @($registeredTask, $folder, $service)) {
            if ($null -ne $comObject -and
                [Runtime.InteropServices.Marshal]::IsComObject($comObject)) {
                [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject(
                    $comObject
                )
            }
        }
    }
}

function Set-TaskSecurityDescriptor {
    param(
        [Parameter(Mandatory)][string]$RegisteredTaskName,
        [Parameter(Mandatory)][string]$SecurityDescriptor
    )

    $service = New-Object -ComObject 'Schedule.Service'
    $folder = $null
    $registeredTask = $null
    try {
        $service.Connect()
        $folder = $service.GetFolder('\')
        $registeredTask = $folder.GetTask($RegisteredTaskName)
        $registeredTask.SetSecurityDescriptor($SecurityDescriptor, 0)
    }
    finally {
        foreach ($comObject in @($registeredTask, $folder, $service)) {
            if ($null -ne $comObject -and
                [Runtime.InteropServices.Marshal]::IsComObject($comObject)) {
                [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject(
                    $comObject
                )
            }
        }
    }
}

if (-not (Test-Path -LiteralPath $sourceExecutable)) {
    throw "The CPU sensor package is incomplete: $sourceExecutable is missing."
}

if ([string]::IsNullOrWhiteSpace($TargetUserSid)) {
    $TargetUserSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
}
try {
    $targetSid = [Security.Principal.SecurityIdentifier]::new($TargetUserSid)
    $TargetUserSid = $targetSid.Value
}
catch [ArgumentException] {
    throw "The target Windows user SID is invalid: $TargetUserSid"
}

if (-not (Test-IsAdministrator)) {
    $powerShell = (Get-Process -Id $PID).Path
    $arguments = (
        '-NoLogo -NoProfile -ExecutionPolicy Bypass -File "{0}" ' +
        '-Elevated -TargetUserSid "{1}"{2}'
    ) -f (
        $MyInvocation.MyCommand.Path.Replace('"', '""')
    ), $TargetUserSid, $(if ($NoStart) { ' -NoStart' } else { '' })
    $process = Start-Process `
        -FilePath $powerShell `
        -Verb RunAs `
        -ArgumentList $arguments `
        -Wait `
        -PassThru
    exit $process.ExitCode
}

$pawnIo = Get-CimInstance Win32_SystemDriver -Filter "Name='PawnIO'" -ErrorAction SilentlyContinue
if ($null -eq $pawnIo) {
    throw 'PawnIO is required for Ryzen 9000 temperature access. Install the signed official edition from https://pawnio.eu/, then run this setup again.'
}

$programFiles = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::ProgramFiles
)
$destination = Join-Path $programFiles 'OPS Monitor Sensor'
if (-not (Test-IsChildPath -Path $destination -Parent $programFiles)) {
    throw "Refusing to install the elevated sensor outside $programFiles."
}
$dataDirectory = Join-Path (Join-Path $destination 'Data') $TargetUserSid
$targetData = [pscustomobject]@{
    Directory = $dataDirectory
    ReadingPath = Join-Path $dataDirectory 'cpu-temperature.txt'
    DiagnosticPath = Join-Path $dataDirectory 'cpu-temperature-diagnostic.txt'
}
if (-not (Test-IsChildPath -Path $dataDirectory -Parent $destination)) {
    throw "Refusing to publish sensor data outside $destination."
}

$stage = Join-Path $programFiles (
    '.OPS-Monitor-Sensor-installing-' + [Guid]::NewGuid().ToString('N')
)
$backup = Join-Path $programFiles (
    '.OPS-Monitor-Sensor-previous-' + [DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff')
)
$backupCreated = $false
$installedNewDestination = $false
$secureTaskBefore = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
$secureTaskExistedBefore = $null -ne $secureTaskBefore
$secureTaskWasRunning =
    $secureTaskExistedBefore -and $secureTaskBefore.State -eq 'Running'
$secureTaskXmlBefore = if ($secureTaskExistedBefore) {
    Export-ScheduledTask -TaskName $taskName
}
else {
    $null
}
$secureTaskSddlBefore = if ($secureTaskExistedBefore) {
    Get-TaskSecurityDescriptor -RegisteredTaskName $taskName
}
else {
    $null
}

try {
    if ($secureTaskWasRunning) {
        Stop-ScheduledTask -TaskName $taskName
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

    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    Copy-Item -Path (Join-Path $sourceDirectory '*') -Destination $stage -Recurse -Force
    $stagedExecutable = Join-Path $stage 'OpsMonitor.SensorBridge.exe'
    if (-not (Test-Path -LiteralPath $stagedExecutable)) {
        throw 'The staged CPU sensor package is incomplete.'
    }

    if (Test-Path -LiteralPath $destination) {
        Move-Item -LiteralPath $destination -Destination $backup
        $backupCreated = $true
    }

    try {
        Move-Item -LiteralPath $stage -Destination $destination
        $installedNewDestination = $true
    }
    catch {
        if ($backupCreated -and (Test-Path -LiteralPath $backup)) {
            Move-Item -LiteralPath $backup -Destination $destination
            $backupCreated = $false
        }
        throw
    }

    $installedExecutable = Join-Path $destination 'OpsMonitor.SensorBridge.exe'
    $dataRoot = Split-Path -Parent $targetData.Directory
    New-Item -ItemType Directory -Path $dataRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $targetData.Directory -Force | Out-Null
    foreach ($secureDataPath in @($dataRoot, $targetData.Directory)) {
        $dataItem = Get-Item -LiteralPath $secureDataPath -Force
        if (($dataItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to use a reparse point for sensor data: $secureDataPath"
        }
    }

    $inheritance =
        [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
        [Security.AccessControl.InheritanceFlags]::ObjectInherit
    $dataAcl = [Security.AccessControl.DirectorySecurity]::new()
    $dataAcl.SetAccessRuleProtection($true, $false)
    foreach ($access in @(
            [pscustomobject]@{
                Identity = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
                Rights = [Security.AccessControl.FileSystemRights]::FullControl
            },
            [pscustomobject]@{
                Identity = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
                Rights = [Security.AccessControl.FileSystemRights]::FullControl
            },
            [pscustomobject]@{
                Identity = $targetSid
                Rights = [Security.AccessControl.FileSystemRights]::ReadAndExecute
            }
        )) {
        $dataAcl.AddAccessRule(
            [Security.AccessControl.FileSystemAccessRule]::new(
                $access.Identity,
                $access.Rights,
                $inheritance,
                [Security.AccessControl.PropagationFlags]::None,
                [Security.AccessControl.AccessControlType]::Allow
            )
        )
    }
    Set-Acl -LiteralPath $targetData.Directory -AclObject $dataAcl

    $taskArguments = '--output "{0}" --diagnostic "{1}"' -f (
        $targetData.ReadingPath
    ), $targetData.DiagnosticPath
    $action = New-ScheduledTaskAction `
        -Execute $installedExecutable `
        -Argument $taskArguments `
        -WorkingDirectory $destination
    $principal = New-ScheduledTaskPrincipal `
        -UserId 'SYSTEM' `
        -LogonType ServiceAccount `
        -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit ([TimeSpan]::Zero) `
        -MultipleInstances IgnoreNew

    Register-ScheduledTask `
        -TaskName $taskName `
        -Action $action `
        -Principal $principal `
        -Settings $settings `
        -Description 'Publishes a validated AMD CPU package temperature for OPS Monitor.' `
        -Force | Out-Null

    Grant-TaskRunAccess `
        -RegisteredTaskName $taskName `
        -SecurityIdentifier $targetSid

    $legacyTask = Get-ScheduledTask -TaskName $legacyTaskName -ErrorAction SilentlyContinue
    if ($null -ne $legacyTask) {
        if ($legacyTask.State -eq 'Running') {
            Stop-ScheduledTask -TaskName $legacyTaskName
        }
        Unregister-ScheduledTask -TaskName $legacyTaskName -Confirm:$false
    }

    if (-not $NoStart) {
        $startedUtc = [DateTime]::UtcNow
        Start-ScheduledTask -TaskName $taskName
        $readingPath = $targetData.ReadingPath
        $confirmed = $false
        for ($attempt = 0; $attempt -lt 30; $attempt++) {
            Start-Sleep -Milliseconds 500
            if (-not (Test-Path -LiteralPath $readingPath)) {
                continue
            }

            $fields = (Get-Content -LiteralPath $readingPath -Raw).Trim().Split('|')
            if ($fields.Length -ne 2) {
                continue
            }

            $temperature = 0.0
            $ticks = 0L
            if (-not [double]::TryParse(
                    $fields[0],
                    [Globalization.NumberStyles]::Float,
                    [Globalization.CultureInfo]::InvariantCulture,
                    [ref]$temperature
                ) -or
                -not [long]::TryParse($fields[1], [ref]$ticks)) {
                continue
            }

            try {
                $publishedUtc = [DateTime]::new($ticks, [DateTimeKind]::Utc)
            }
            catch [ArgumentOutOfRangeException] {
                continue
            }

            if ($publishedUtc -ge $startedUtc.AddSeconds(-1) -and
                $temperature -ge 5 -and
                $temperature -le 125) {
                Write-Host (
                    'Live CPU package temperature: {0:0.0} °C' -f $temperature
                ) -ForegroundColor Green
                $confirmed = $true
                break
            }
        }

        if (-not $confirmed) {
            $diagnosticPath = $targetData.DiagnosticPath
            $detail = if (Test-Path -LiteralPath $diagnosticPath) {
                (Get-Content -LiteralPath $diagnosticPath -Raw).Trim()
            }
            else {
                'No fresh reading was published within 15 seconds.'
            }
            throw "The secure CPU sensor was installed but did not produce a valid reading. $detail"
        }
    }

    if ($backupCreated -and (Test-Path -LiteralPath $backup)) {
        Remove-Item -LiteralPath $backup -Recurse -Force
        $backupCreated = $false
    }
}
catch {
    $failure = $_
    $secureTask = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if ($null -ne $secureTask -and $secureTask.State -eq 'Running') {
        Stop-ScheduledTask -TaskName $taskName
    }
    if (-not $secureTaskExistedBefore -and $null -ne $secureTask) {
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    }

    if ($installedNewDestination -and (Test-Path -LiteralPath $destination)) {
        Remove-Item -LiteralPath $destination -Recurse -Force
    }
    if ($backupCreated -and (Test-Path -LiteralPath $backup)) {
        Move-Item -LiteralPath $backup -Destination $destination
        $backupCreated = $false
    }

    if ($secureTaskExistedBefore -and
        -not [string]::IsNullOrWhiteSpace($secureTaskXmlBefore)) {
        Register-ScheduledTask `
            -TaskName $taskName `
            -Xml $secureTaskXmlBefore `
            -Force | Out-Null
        if (-not [string]::IsNullOrWhiteSpace($secureTaskSddlBefore)) {
            Set-TaskSecurityDescriptor `
                -RegisteredTaskName $taskName `
                -SecurityDescriptor $secureTaskSddlBefore
        }
        if ($secureTaskWasRunning -and (Test-Path -LiteralPath $destination)) {
            Start-ScheduledTask -TaskName $taskName
        }
    }

    throw $failure
}
finally {
    if (Test-Path -LiteralPath $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
}

Write-Host "OPS Monitor's secure CPU temperature sensor is enabled." -ForegroundColor Green
Write-Host "Sensor files: $destination" -ForegroundColor Cyan
Write-Host 'PawnIO was preserved because other hardware-monitoring apps may use it.' -ForegroundColor Cyan
