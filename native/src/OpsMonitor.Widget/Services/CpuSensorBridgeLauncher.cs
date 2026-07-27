using System.Diagnostics;
using System.Globalization;
using System.IO;
using OpsMonitor.Core.Diagnostics;
using OpsMonitor.Core.Providers;

namespace OpsMonitor.Widget.Services;

internal static class CpuSensorBridgeLauncher
{
    private const string TaskName = "OPS Monitor CPU Sensor";
    private const string BridgeProcessName = "OpsMonitor.SensorBridge";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaximumReadingAge = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan MaximumFutureSkew = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);
    private static readonly Lock Gate = new();
    private static CancellationTokenSource? _watchdogCancellation;
    private static Task? _watchdogTask;
    private static long _monitoringGeneration;
    private static int _lastReportedOutcome = -1;

    public static void StartMonitoring()
    {
        lock (Gate)
        {
            if (_watchdogCancellation is not null)
            {
                return;
            }

            var cancellation = new CancellationTokenSource();
            long generation = unchecked(++_monitoringGeneration);
            _watchdogCancellation = cancellation;
            _watchdogTask = Task.Run(
                () => MonitorAsync(generation, cancellation.Token));
        }
    }

    public static void StopMonitoring()
    {
        CancellationTokenSource? cancellation;
        Task? watchdogTask;
        lock (Gate)
        {
            cancellation = _watchdogCancellation;
            watchdogTask = _watchdogTask;
            if (cancellation is null)
            {
                return;
            }

            _watchdogCancellation = null;
            _watchdogTask = null;
            _ = unchecked(++_monitoringGeneration);
            cancellation.Cancel();
        }

        bool completed = watchdogTask is null;
        try
        {
            completed = watchdogTask?.Wait(ShutdownTimeout) != false;
            if (!completed)
            {
                AppDiagnostics.Warning(
                    "CPU sensor watchdog did not stop within its 3 s shutdown limit.");
            }
        }
        catch (AggregateException exception) when (
            exception.InnerExceptions.All(
                static inner => inner is OperationCanceledException))
        {
            completed = true;
        }
        finally
        {
            if (completed)
            {
                cancellation.Dispose();
            }
            else if (watchdogTask is not null)
            {
                _ = watchdogTask.ContinueWith(
                    static (_, state) =>
                        ((CancellationTokenSource)state!).Dispose(),
                    cancellation,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
    }

    internal static bool IsReadingFresh(
        string readingPath,
        DateTimeOffset timestampUtc,
        TimeSpan maximumAge)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(readingPath);
        if (maximumAge <= TimeSpan.Zero)
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(
                readingPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                512,
                FileOptions.SequentialScan);
            using var reader = new StreamReader(stream);
            string[] fields = reader.ReadToEnd().Trim().Split('|');
            if (fields.Length != 2 ||
                !double.TryParse(
                    fields[0],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double temperature) ||
                !double.IsFinite(temperature) ||
                temperature is < 5 or > 125 ||
                !long.TryParse(
                    fields[1],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long ticks) ||
                ticks < DateTime.MinValue.Ticks ||
                ticks > DateTime.MaxValue.Ticks)
            {
                return false;
            }

            var capturedUtc = new DateTimeOffset(
                new DateTime(ticks, DateTimeKind.Utc));
            TimeSpan age = timestampUtc - capturedUtc;
            return age >= -MaximumFutureSkew && age <= maximumAge;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            NotSupportedException or
            System.Security.SecurityException)
        {
            return false;
        }
    }

    private static async Task MonitorAsync(
        long generation,
        CancellationToken cancellationToken)
    {
        var policy = new CpuSensorRecoveryPolicy();
        long startedAt = Stopwatch.GetTimestamp();

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);
                try
                {
                    await CheckAndRecoverAsync(
                            policy,
                            elapsed,
                            generation,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    policy.RecordAttempt(elapsed);
                    ReportOutcome(
                        3,
                        $"CPU sensor recovery check failed: {exception.Message}",
                        warning: true);
                }

                await Task.Delay(CheckInterval, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
    }

    private static async Task CheckAndRecoverAsync(
        CpuSensorRecoveryPolicy policy,
        TimeSpan elapsed,
        long generation,
        CancellationToken cancellationToken)
    {
        string readingPath = CpuTemperatureBridgeProvider.GetDefaultReadingPath();
        if (IsReadingFresh(
                readingPath,
                DateTimeOffset.UtcNow,
                MaximumReadingAge))
        {
            policy.RecordHealthy();
            Interlocked.Exchange(ref _lastReportedOutcome, -1);
            return;
        }

        if (!policy.IsDue(elapsed))
        {
            return;
        }

        string installedBridgePath = GetInstalledBridgePath();
        if (string.IsNullOrWhiteSpace(installedBridgePath) ||
            !File.Exists(installedBridgePath))
        {
            policy.RecordCapabilityUnavailable(elapsed);
            ReportOutcome(
                1,
                "Optional CPU temperature sensor is not enabled; automatic recovery is idle.",
                warning: false);
            return;
        }

        bool bridgeRunning = IsBridgeRunning();
        CpuSensorRecoveryAction action = policy.GetAction(
            elapsed,
            bridgeRunning);
        if (action == CpuSensorRecoveryAction.None)
        {
            return;
        }

        TaskCommandResult query = await RunTaskSchedulerAsync(
                $"/Query /TN \"{TaskName}\"",
                generation,
                cancellationToken)
            .ConfigureAwait(false);
        if (!query.Succeeded)
        {
            if (query.Status == TaskCommandStatus.Completed)
            {
                policy.RecordCapabilityUnavailable(elapsed);
                ReportOutcome(
                    1,
                    "Optional CPU temperature task is not installed or accessible; " +
                    "automatic recovery is idle.",
                    warning: false);
            }
            else
            {
                policy.RecordAttempt(elapsed);
                ReportCommandFailure("query", query);
            }

            return;
        }

        TaskCommandResult result;
        TaskCommandResult? endResult = null;
        if (action == CpuSensorRecoveryAction.RestartTask)
        {
            endResult = await RunTaskSchedulerAsync(
                    $"/End /TN \"{TaskName}\"",
                    generation,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        result = await RunTaskSchedulerAsync(
                $"/Run /TN \"{TaskName}\"",
                generation,
                cancellationToken)
            .ConfigureAwait(false);
        policy.RecordAttempt(elapsed);

        if (result.Succeeded && endResult is null or { Succeeded: true })
        {
            ReportOutcome(
                0,
                action == CpuSensorRecoveryAction.RestartTask
                    ? "Stale CPU sensor task was restarted."
                    : "CPU sensor task start or recovery requested.",
                warning: false);
            return;
        }

        if (result.Succeeded && endResult is { Succeeded: false })
        {
            ReportOutcome(
                4,
                "CPU sensor start was requested, but its stale task could not be " +
                $"ended first: {endResult.Value.Detail}",
                warning: true);
            return;
        }

        ReportCommandFailure(
            action == CpuSensorRecoveryAction.RestartTask ? "restart" : "start",
            result);
    }

    private static string GetInstalledBridgePath()
    {
        string programFiles =
            Environment.GetEnvironmentVariable("ProgramW6432") ??
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        return string.IsNullOrWhiteSpace(programFiles)
            ? string.Empty
            : Path.Combine(
                programFiles,
                "OPS Monitor Sensor",
                "OpsMonitor.SensorBridge.exe");
    }

    private static bool IsBridgeRunning()
    {
        try
        {
            foreach (Process process in Process.GetProcessesByName(
                         BridgeProcessName))
            {
                using (process)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            return true;
                        }
                    }
                    catch (Exception exception) when (
                        exception is InvalidOperationException or
                        System.ComponentModel.Win32Exception or
                        NotSupportedException)
                    {
                        // If Windows denies inspection, assume the elevated
                        // process is alive and give it the stale grace period.
                        return true;
                    }
                }
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            System.ComponentModel.Win32Exception or
            NotSupportedException)
        {
            return true;
        }

        return false;
    }

    private static async Task<TaskCommandResult> RunTaskSchedulerAsync(
        string arguments,
        long generation,
        CancellationToken cancellationToken)
    {
        Process? process = null;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(
                    Environment.SystemDirectory,
                    "schtasks.exe"),
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            lock (Gate)
            {
                if (!IsActiveGenerationUnsafe(
                        generation,
                        cancellationToken))
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                // Starting the child under the same gate used by
                // StopMonitoring makes the ordering strict: either this
                // process starts first, or shutdown wins and no process starts.
                process = Process.Start(startInfo);
            }

            if (process is null)
            {
                return TaskCommandResult.StartFailed(
                    "Task Scheduler launcher did not start.");
            }

            using (process)
            {
                // Keep draining redirected pipes even when the watchdog is
                // cancelled so the child can always exit without a full
                // output buffer blocking it.
                Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(
                    CancellationToken.None);
                Task<string> standardError = process.StandardError.ReadToEndAsync(
                    CancellationToken.None);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                timeout.CancelAfter(CommandTimeout);
                try
                {
                    await process.WaitForExitAsync(timeout.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    TryKill(process);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }

                    return TaskCommandResult.TimedOut(
                        "Task Scheduler launcher exceeded its 2.5 s limit.");
                }

                string output = await standardOutput.ConfigureAwait(false);
                string error = await standardError.ConfigureAwait(false);
                string detail = string.IsNullOrWhiteSpace(error)
                    ? output.Trim()
                    : error.Trim();
                return TaskCommandResult.Completed(
                    process.ExitCode,
                    detail);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            if (process is not null)
            {
                TryKill(process);
            }

            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            System.ComponentModel.Win32Exception or
            IOException or
            UnauthorizedAccessException or
            ObjectDisposedException)
        {
            return TaskCommandResult.StartFailed(exception.Message);
        }
    }

    private static bool IsActiveGenerationUnsafe(
        long generation,
        CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested &&
        _watchdogCancellation is not null &&
        generation == _monitoringGeneration;

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            System.ComponentModel.Win32Exception or
            NotSupportedException)
        {
            // The child may have exited between inspection and termination.
        }
    }

    private static void ReportCommandFailure(
        string operation,
        TaskCommandResult result)
    {
        int outcome = result.Status == TaskCommandStatus.TimedOut ? 2 : 3;
        ReportOutcome(
            outcome,
            $"CPU sensor task {operation} failed: {result.Detail}",
            warning: true);
    }

    private static void ReportOutcome(int outcome, string message, bool warning)
    {
        if (Interlocked.Exchange(ref _lastReportedOutcome, outcome) == outcome)
        {
            return;
        }

        if (warning)
        {
            AppDiagnostics.Warning(message);
        }
        else
        {
            AppDiagnostics.Information(message);
        }
    }
}

internal enum CpuSensorRecoveryAction
{
    None,
    StartTask,
    RestartTask
}

internal sealed class CpuSensorRecoveryPolicy
{
    internal static readonly TimeSpan RunningBridgeGracePeriod =
        TimeSpan.FromSeconds(20);
    internal static readonly TimeSpan CapabilityRetryDelay =
        TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan MaximumRetryDelay =
        TimeSpan.FromMinutes(1);

    private int _consecutiveAttempts;
    private TimeSpan _nextAttemptAt;
    private TimeSpan? _runningStaleSince;

    internal int ConsecutiveAttempts => _consecutiveAttempts;

    internal bool IsDue(TimeSpan elapsed) => elapsed >= _nextAttemptAt;

    internal CpuSensorRecoveryAction GetAction(
        TimeSpan elapsed,
        bool bridgeRunning)
    {
        if (!IsDue(elapsed))
        {
            return CpuSensorRecoveryAction.None;
        }

        if (!bridgeRunning)
        {
            _runningStaleSince = null;
            return CpuSensorRecoveryAction.StartTask;
        }

        _runningStaleSince ??= elapsed;
        TimeSpan restartAt =
            _runningStaleSince.Value + RunningBridgeGracePeriod;
        if (elapsed < restartAt)
        {
            _nextAttemptAt = restartAt;
            return CpuSensorRecoveryAction.None;
        }

        return CpuSensorRecoveryAction.RestartTask;
    }

    internal void RecordAttempt(TimeSpan elapsed)
    {
        _consecutiveAttempts = Math.Min(
            _consecutiveAttempts + 1,
            31);
        _nextAttemptAt = elapsed + GetRetryDelay(_consecutiveAttempts);
        _runningStaleSince = elapsed;
    }

    internal void RecordCapabilityUnavailable(TimeSpan elapsed)
    {
        _consecutiveAttempts = 0;
        _runningStaleSince = null;
        _nextAttemptAt = elapsed + CapabilityRetryDelay;
    }

    internal void RecordHealthy()
    {
        _consecutiveAttempts = 0;
        _runningStaleSince = null;
        _nextAttemptAt = TimeSpan.Zero;
    }

    internal static TimeSpan GetRetryDelay(int consecutiveAttempt)
    {
        if (consecutiveAttempt <= 0)
        {
            return TimeSpan.Zero;
        }

        int exponent = Math.Min(consecutiveAttempt - 1, 4);
        double seconds = 5 * (1 << exponent);
        return TimeSpan.FromSeconds(
            Math.Min(seconds, MaximumRetryDelay.TotalSeconds));
    }
}

internal enum TaskCommandStatus
{
    Completed,
    StartFailed,
    TimedOut
}

internal readonly record struct TaskCommandResult(
    TaskCommandStatus Status,
    int ExitCode,
    string Detail)
{
    internal bool Succeeded =>
        Status == TaskCommandStatus.Completed && ExitCode == 0;

    internal static TaskCommandResult Completed(
        int exitCode,
        string detail) =>
        new(
            TaskCommandStatus.Completed,
            exitCode,
            string.IsNullOrWhiteSpace(detail)
                ? $"Task Scheduler exited with code {exitCode}."
                : detail);

    internal static TaskCommandResult StartFailed(string detail) =>
        new(TaskCommandStatus.StartFailed, -1, detail);

    internal static TaskCommandResult TimedOut(string detail) =>
        new(TaskCommandStatus.TimedOut, -1, detail);
}
