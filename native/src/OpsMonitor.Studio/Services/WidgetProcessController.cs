using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace OpsMonitor.Studio.Services;

public sealed record WidgetProcessResult(bool Success, bool Restarted, string Message);

public static class WidgetProcessController
{
    private const string ProcessName = "OpsMonitor.Widget";

    public static string? ExecutablePath => WidgetExecutableLocator.Find();

    public static bool IsRunning
    {
        get
        {
            var processes = Process.GetProcessesByName(ProcessName);
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }
    }

    public static WidgetProcessResult OpenOrRestart()
    {
        var running = Process.GetProcessesByName(ProcessName);
        var restarted = running.Length > 0;
        var executablePath = ExecutablePath;
        if (executablePath is null && running.Length > 0)
        {
            try
            {
                executablePath = running[0].MainModule?.FileName;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // The normal locator result below provides the actionable message.
            }
        }

        if (executablePath is null)
        {
            foreach (var process in running)
            {
                process.Dispose();
            }

            return new WidgetProcessResult(
                false,
                false,
                "Widget executable was not found. Build OpsMonitor.Widget or set OPS_MONITOR_WIDGET_PATH.");
        }

        if (restarted)
        {
            foreach (var process in running)
            {
                using (process)
                {
                    if (process.MainWindowHandle != 0)
                    {
                        _ = process.CloseMainWindow();
                    }

                    if (!process.WaitForExit(1_500))
                    {
                        process.Kill(entireProcessTree: false);
                        process.WaitForExit(1_500);
                    }
                }
            }
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(executablePath)!,
            });

            return process is null
                ? new WidgetProcessResult(false, restarted, "Windows did not start the widget.")
                : new WidgetProcessResult(
                    true,
                    restarted,
                    restarted ? "Widget restarted with current settings." : "Widget opened.");
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new WidgetProcessResult(false, restarted, $"Widget could not start: {exception.Message}");
        }
    }

    public static bool TryBringToFront()
    {
        var process = Process.GetProcessesByName(ProcessName)
            .FirstOrDefault(item => item.MainWindowHandle != 0);
        if (process is null)
        {
            return false;
        }

        using (process)
        {
            return SetForegroundWindow(process.MainWindowHandle);
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint windowHandle);
}

public static class WidgetExecutableLocator
{
    public static string? Find()
    {
        var configuredPath = Environment.GetEnvironmentVariable("OPS_MONITOR_WIDGET_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var expanded = Environment.ExpandEnvironmentVariables(configuredPath);
            if (File.Exists(expanded))
            {
                return Path.GetFullPath(expanded);
            }
        }

        var baseDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var level = 0; level < 7 && baseDirectory is not null; level++)
        {
            var candidates = new[]
            {
                Path.Combine(baseDirectory.FullName, "OpsMonitor.Widget.exe"),
                Path.Combine(
                    baseDirectory.FullName,
                    "OpsMonitor.Widget",
                    "bin",
                    "Debug",
                    "net10.0-windows",
                    "OpsMonitor.Widget.exe"),
                Path.Combine(
                    baseDirectory.FullName,
                    "native",
                    "src",
                    "OpsMonitor.Widget",
                    "bin",
                    "Debug",
                    "net10.0-windows",
                    "OpsMonitor.Widget.exe"),
            };

            var match = candidates.FirstOrDefault(File.Exists);
            if (match is not null)
            {
                return Path.GetFullPath(match);
            }

            baseDirectory = baseDirectory.Parent;
        }

        return null;
    }
}
