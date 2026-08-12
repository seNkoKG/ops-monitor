[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Executable,

    [string[]]$ArgumentList = @(),

    [ValidateRange(1, 60)]
    [int]$WarmupSeconds = 5,

    [ValidateRange(5, 120)]
    [int]$SampleSeconds = 15,

    [string]$JsonOutputPath,

    [ValidateRange(0, 100)]
    [double]$MaximumCpuPercentWholeMachine = 0,

    [ValidateRange(0, 4096)]
    [double]$MaximumWorkingSetPeakMB = 0,

    [ValidateRange(0, 100000)]
    [int]$MaximumHandlesPeak = 0,

    [ValidateRange(0, 10000)]
    [int]$MaximumThreadsPeak = 0,

    [switch]$FailOnBudgetExceeded
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

    $cpuPercentWholeMachine = [Math]::Round($wholeMachinePercent, 3)
    $workingSetPeakMB = [Math]::Round(($workingSetSamples | Measure-Object -Maximum).Maximum, 2)
    $handlesPeak = ($handleSamples | Measure-Object -Maximum).Maximum
    $threadsPeak = ($threadSamples | Measure-Object -Maximum).Maximum
    $violations = [Collections.Generic.List[string]]::new()
    if ($MaximumCpuPercentWholeMachine -gt 0 -and
        $cpuPercentWholeMachine -gt $MaximumCpuPercentWholeMachine) {
        $violations.Add("CPU $cpuPercentWholeMachine% exceeds $MaximumCpuPercentWholeMachine%")
    }
    if ($MaximumWorkingSetPeakMB -gt 0 -and $workingSetPeakMB -gt $MaximumWorkingSetPeakMB) {
        $violations.Add("working set $workingSetPeakMB MB exceeds $MaximumWorkingSetPeakMB MB")
    }
    if ($MaximumHandlesPeak -gt 0 -and $handlesPeak -gt $MaximumHandlesPeak) {
        $violations.Add("handles $handlesPeak exceeds $MaximumHandlesPeak")
    }
    if ($MaximumThreadsPeak -gt 0 -and $threadsPeak -gt $MaximumThreadsPeak) {
        $violations.Add("threads $threadsPeak exceeds $MaximumThreadsPeak")
    }

    $result = [pscustomobject]@{
        Executable = $resolvedExecutable
        ProcessId = $process.Id
        LogicalProcessors = [Environment]::ProcessorCount
        WarmupSeconds = $WarmupSeconds
        SampleSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 2)
        CpuPercentSingleCore = [Math]::Round($singleCorePercent, 3)
        CpuPercentWholeMachine = $cpuPercentWholeMachine
        WorkingSetAverageMB = [Math]::Round(($workingSetSamples | Measure-Object -Average).Average, 2)
        WorkingSetPeakMB = $workingSetPeakMB
        PrivateMemoryAverageMB = [Math]::Round(($privateSamples | Measure-Object -Average).Average, 2)
        PrivateMemoryPeakMB = [Math]::Round(($privateSamples | Measure-Object -Maximum).Maximum, 2)
        HandlesPeak = $handlesPeak
        ThreadsPeak = $threadsPeak
        BudgetPassed = $violations.Count -eq 0
        BudgetViolations = @($violations)
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
    if ($FailOnBudgetExceeded -and $violations.Count -gt 0) {
        throw "Application impact budget failed: $($violations -join '; ')."
    }
}
finally {
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }
}
