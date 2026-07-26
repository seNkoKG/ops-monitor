using System.Diagnostics;
using System.IO;
using OpsMonitor.Core.Diagnostics;

namespace OpsMonitor.Widget.Services;

internal static class CpuSensorBridgeLauncher
{
    private const string TaskName = "OPS Monitor CPU Sensor";
    private static int _launchRequested;

    public static void TryStartInBackground()
    {
        if (Interlocked.Exchange(ref _launchRequested, 1) != 0)
        {
            return;
        }

        _ = Task.Run(TryStart);
    }

    private static void TryStart()
    {
        try
        {
            var taskScheduler = Path.Combine(
                Environment.SystemDirectory,
                "schtasks.exe");
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = taskScheduler,
                Arguments = $"/Run /TN \"{TaskName}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process is null)
            {
                AppDiagnostics.Warning("CPU sensor task launcher did not start.");
                return;
            }

            if (!process.WaitForExit(2_500))
            {
                process.Kill(entireProcessTree: true);
                AppDiagnostics.Warning("CPU sensor task launcher exceeded its 2.5 s limit.");
                return;
            }

            if (process.ExitCode == 0)
            {
                AppDiagnostics.Information("CPU sensor task start requested.");
            }
            else
            {
                AppDiagnostics.Information(
                    "Optional CPU sensor task is not installed or could not be started.");
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            System.ComponentModel.Win32Exception or
            IOException or
            UnauthorizedAccessException)
        {
            AppDiagnostics.Warning(
                $"Optional CPU sensor task launch failed: {exception.Message}");
        }
    }
}
