[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Executable,

    [string[]]$ArgumentList = @(),

    [ValidateRange(1, 60)]
    [int]$WarmupSeconds = 5,

    [ValidateRange(5, 120)]
    [int]$SampleSeconds = 15,

    [string]$JsonOutputPath
)

$ErrorActionPreference = 'Stop'
$resolvedExecutable = (Resolve-Path -LiteralPath $Executable).Path
$startParameters = @{
    FilePath = $resolvedExecutable
    WorkingDirectory = Split-Path -Parent $resolvedExecutable
    PassThru = $true
}
if ($ArgumentList.Count -gt 0) {
    $startParameters.ArgumentList = $ArgumentList
}

$process = Start-Process @startParameters

try {
    Start-Sleep -Seconds $WarmupSeconds
    $process.Refresh()
    if ($process.HasExited) {
        throw "Application exited during warmup with code $($process.ExitCode)."
    }

    $initialCpu = $process.TotalProcessorTime.TotalSeconds
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $workingSetSamples = [Collections.Generic.List[double]]::new()
    $privateSamples = [Collections.Generic.List[double]]::new()
    $handleSamples = [Collections.Generic.List[int]]::new()
    $threadSamples = [Collections.Generic.List[int]]::new()

    while ($stopwatch.Elapsed.TotalSeconds -lt $SampleSeconds) {
        Start-Sleep -Milliseconds 500
        $process.Refresh()
        if ($process.HasExited) {
            throw "Application exited during measurement with code $($process.ExitCode)."
        }

        $workingSetSamples.Add($process.WorkingSet64 / 1MB)
        $privateSamples.Add($process.PrivateMemorySize64 / 1MB)
        $handleSamples.Add($process.HandleCount)
        $threadSamples.Add($process.Threads.Count)
    }

    $stopwatch.Stop()
    $process.Refresh()
    $cpuSeconds = $process.TotalProcessorTime.TotalSeconds - $initialCpu
    $singleCorePercent = 100.0 * $cpuSeconds / $stopwatch.Elapsed.TotalSeconds
    $wholeMachinePercent = $singleCorePercent / [Environment]::ProcessorCount

    $result = [pscustomobject]@{
        Executable = $resolvedExecutable
        ProcessId = $process.Id
        LogicalProcessors = [Environment]::ProcessorCount
        WarmupSeconds = $WarmupSeconds
        SampleSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 2)
        CpuPercentSingleCore = [Math]::Round($singleCorePercent, 3)
        CpuPercentWholeMachine = [Math]::Round($wholeMachinePercent, 3)
        WorkingSetAverageMB = [Math]::Round(($workingSetSamples | Measure-Object -Average).Average, 2)
        WorkingSetPeakMB = [Math]::Round(($workingSetSamples | Measure-Object -Maximum).Maximum, 2)
        PrivateMemoryAverageMB = [Math]::Round(($privateSamples | Measure-Object -Average).Average, 2)
        PrivateMemoryPeakMB = [Math]::Round(($privateSamples | Measure-Object -Maximum).Maximum, 2)
        HandlesPeak = ($handleSamples | Measure-Object -Maximum).Maximum
        ThreadsPeak = ($threadSamples | Measure-Object -Maximum).Maximum
    }

    if (-not [string]::IsNullOrWhiteSpace($JsonOutputPath)) {
        $absoluteJsonPath = [IO.Path]::GetFullPath($JsonOutputPath)
        $jsonDirectory = Split-Path -Parent $absoluteJsonPath
        if (-not (Test-Path -LiteralPath $jsonDirectory)) {
            [void](New-Item -ItemType Directory -Path $jsonDirectory -Force)
        }
        $result | ConvertTo-Json | Set-Content -LiteralPath $absoluteJsonPath -Encoding utf8
    }

    Write-Output $result
}
finally {
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }
}
